## v1.3.3 (patch)

Changes since v1.3.2:

- refactor: walk the scan with EnumerationOptions instead of a manual descent [patch] ([@Claude](https://github.com/Claude))
- fix: survive an unreadable directory and a symlink cycle while scanning [patch] ([@Claude](https://github.com/Claude))
- fix: skip a file the hashing pass cannot read instead of aborting [patch] ([@Claude](https://github.com/Claude))

