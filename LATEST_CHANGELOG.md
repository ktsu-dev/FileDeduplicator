## v1.3.17 (patch)

Changes since v1.3.16:

- Move the delete attempt out of DeleteDuplicates to bring its complexity under the limit ([@Claude](https://github.com/Claude))
- Filter CollapseSameFile's sequence with Where rather than inside the loop ([@Claude](https://github.com/Claude))
- Never treat one file reached at two paths as a duplicate of itself [patch] ([@Claude](https://github.com/Claude))
- Run every verb's discover-and-hash steps through one DuplicateScan ([@Claude](https://github.com/Claude))
- Hash only files whose size another file shares [patch] ([@Claude](https://github.com/Claude))

