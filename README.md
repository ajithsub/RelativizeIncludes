# Usage

```console
> relinc --help

RelativizeIncludes 1.0.0.0
Copyright c  2022

  -p, --path           The root path of the directory containing all source files to process. If no path is specified, the current directory is used.

  -d, --dry-run        (Default: false) Perform a dry run and print out potential replacements.

  -s, --staging        Copy modified source files to the specified path. This should be an empty directory.

  -i, --ignore-case    (Default: false) Ignore case when searching for matching header files by name.

  --help               Display this help screen.

  --version            Display version information.
```