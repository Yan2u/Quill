//! Native rendering for Quill (M6.3): Mermaid diagrams as simplified SVG.
//!
//! The C ABI: `quill_mermaid_render` renders Mermaid source to SVG with mermaid-rs-renderer, then
//! parses and writes it again with usvg, which resolves styles and turns text into paths with the
//! system fonts, so the result needs only the SVG subset Direct2D supports. The output buffer is
//! freed with `quill_buffer_free`. Calls may come from any thread; the font database loads once.

use std::panic::{AssertUnwindSafe, catch_unwind};
use std::ptr;
use std::sync::{Arc, OnceLock};

use mermaid_rs_renderer::{RenderOptions, Theme, render_with_options};

/// Success: the output is SVG.
pub const STATUS_OK: i32 = 0;
/// The source does not render: the output is the error message.
pub const STATUS_ERROR: i32 = 1;
/// A panic inside the renderer; no output.
pub const STATUS_PANIC: i32 = 2;
/// Invalid arguments; no output.
pub const STATUS_ARGUMENTS: i32 = -1;

static FONTS: OnceLock<Arc<usvg::fontdb::Database>> = OnceLock::new();

fn fonts() -> Arc<usvg::fontdb::Database> {
    FONTS
        .get_or_init(|| {
            let mut fonts = usvg::fontdb::Database::new();
            fonts.load_system_fonts();
            fonts.set_serif_family("Times New Roman");
            fonts.set_sans_serif_family("Segoe UI");
            fonts.set_monospace_family("Cascadia Mono");
            Arc::new(fonts)
        })
        .clone()
}

/// Renders Mermaid source to simplified SVG and its size in pixels.
pub fn render(source: &str, dark: bool) -> Result<(String, f32, f32), String> {
    let mut options = RenderOptions::default();
    if dark {
        options.theme = Theme::dark();
    }

    // The diagram lies on the page: no background of its own (the dark theme's is #333).
    options.theme.background = "transparent".to_string();

    let rendered = render_with_options(source, options).map_err(|error| error.to_string())?;
    let mut parse = usvg::Options::default();
    parse.font_family = "Segoe UI".to_string();
    parse.languages = vec!["en".to_string(), "zh-CN".to_string()];
    parse.fontdb = fonts();
    // Diagram SVG never needs outside resources: no file reads or fetches from what a document says.
    parse.image_href_resolver = usvg::ImageHrefResolver {
        resolve_data: usvg::ImageHrefResolver::default_data_resolver(),
        resolve_string: Box::new(|_, _| None),
    };

    let tree = usvg::Tree::from_str(&rendered, &parse).map_err(|error| error.to_string())?;
    let size = tree.size();
    Ok((tree.to_string(&usvg::WriteOptions::default()), size.width(), size.height()))
}

fn write_output(value: String, output: *mut *mut u8, output_len: *mut usize) {
    let data = value.into_bytes().into_boxed_slice();
    let len = data.len();
    unsafe {
        *output = Box::into_raw(data) as *mut u8;
        *output_len = len;
    }
}

/// Renders UTF-8 Mermaid source. Returns a `STATUS_*` value; on `STATUS_OK` the output is SVG and the
/// size is set, on `STATUS_ERROR` the output is the message.
#[unsafe(no_mangle)]
pub extern "C" fn quill_mermaid_render(
    input: *const u8,
    input_len: usize,
    dark: bool,
    output: *mut *mut u8,
    output_len: *mut usize,
    width: *mut f32,
    height: *mut f32,
) -> i32 {
    if input.is_null() || output.is_null() || output_len.is_null() || width.is_null() || height.is_null() {
        return STATUS_ARGUMENTS;
    }

    unsafe {
        *output = ptr::null_mut();
        *output_len = 0;
        *width = 0.0;
        *height = 0.0;
    }

    catch_unwind(AssertUnwindSafe(|| {
        let bytes = unsafe { std::slice::from_raw_parts(input, input_len) };
        let result = std::str::from_utf8(bytes)
            .map_err(|error| error.to_string())
            .and_then(|source| render(source, dark));
        match result {
            Ok((svg, w, h)) => {
                unsafe {
                    *width = w;
                    *height = h;
                }
                write_output(svg, output, output_len);
                STATUS_OK
            }
            Err(message) => {
                write_output(message, output, output_len);
                STATUS_ERROR
            }
        }
    }))
    .unwrap_or(STATUS_PANIC)
}

/// Frees a buffer `quill_mermaid_render` returned.
#[unsafe(no_mangle)]
pub extern "C" fn quill_buffer_free(data: *mut u8, len: usize) {
    if !data.is_null() {
        unsafe { drop(Box::from_raw(ptr::slice_from_raw_parts_mut(data, len))) };
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn flowchart_renders_text_as_paths() {
        let (svg, width, height) = render("flowchart TD\n  A[Start] --> B[Finish]", false).unwrap();
        assert!(width > 10.0 && height > 10.0);
        assert!(svg.contains("<path"));
        assert!(!svg.contains("<text"));
    }

    #[test]
    fn invalid_source_is_an_error() {
        assert!(render("flowchart TD\n  A[Start --> ", false).is_err() || render("not a diagram at all", false).is_err());
    }
}
