# GitHub Flavored Markdown examples

| File | Source | SHA-256 |
| --- | --- | --- |
| `spec.txt` | https://raw.githubusercontent.com/github/cmark-gfm/27d942c8b0a62d192f616e5bf3578f4b6a89e180/test/spec.txt | `7d8e5814befec287ac116786d81ff14e0adc9b13295b4494649e995408fd871c` |

- GFM spec 0.29-gfm (2019-04-06) as kept in the cmark-gfm repository at commit
  `27d942c8b0a62d192f616e5bf3578f4b6a89e180`; the same commit and hash are pinned by Folia
  (`.agents/refs/Folia/tools/markdown-conformance/gfm-sources.lock.json`).
- 672 examples, each between a line of 32 backticks followed by ` example` (optionally an extension name:
  `table`, `strikethrough`, `autolink`, `tagfilter`, `disabled`) and a closing line of 32 backticks; a line
  holding a single `.` separates the Markdown from the expected HTML. In the Markdown part, `→` stands for a
  tab character, as in the CommonMark spec tooling.

## License

The GitHub Flavored Markdown Spec is based on the CommonMark Spec (copyright John MacFarlane) and is licensed
under the Creative Commons Attribution-ShareAlike 4.0 International license
(https://creativecommons.org/licenses/by-sa/4.0/). The examples are used unmodified as test input.
