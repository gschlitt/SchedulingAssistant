# Section import template

`make_template.py` builds the workbook that department administrators fill in for **Files, Import Sections…**:

    src/TermPoint/Assets/Templates/TermPointSectionsTemplate.xlsx

It has two sheets:

- **Sections**: the 18 template columns, a few sample rows showing every pattern (two instructors, an odd-week lab,
  one section on two rows, an online section), and Text formatting on Course Number, Section Code, Begin and End so
  Excel keeps leading zeros.
- **Instructions**: the text of `SectionImportInstructions.txt` (see below), one paragraph per row.

## The instructions text

The instructions live in one file, which is the single source for three uses:

    src/TermPoint/Assets/Templates/SectionImportInstructions.txt

- The **Instructions sheet** of the workbook (built by `make_template.py`).
- The **Import Sections flyout**, which shows the text under step 2 ("Show instructions") and again on its
  Problems screen. The file ships inside the app as an `avares://` resource (the `Assets/**` glob of the project file).
- The **"Save instructions..." button**, which saves the same text as a web page (`.html`) that any browser can
  open and print.

Markup (the same rules in the script's `parse_instructions` and the app's `ImportInstructions.Parse`): a line
starting `# ` is a heading, `- ` is a bullet (one line each), blank lines separate paragraphs, and consecutive
plain lines are joined into one paragraph.

**After editing that file, regenerate the workbook** (`python tools/section-import/make_template.py`). The checked-in
`.xlsx` carries a copy of the text, and `TemplateWorkbookTests` has a drift test that fails until the workbook is
regenerated, so the three uses can never disagree.

## Run it

    python tools/section-import/make_template.py

It needs openpyxl (`python -m pip install --user openpyxl`). Run it again after editing the instructions file, the
sample rows or the column list. The output is reproducible: unchanged inputs give identical bytes.

## Keep in step with the app

The column list in the script (`COLUMNS`) must match `SectionImportColumns.All` in the app, spelled and ordered
identically. The generated `.xlsx` is checked in (the app loads it as an `avares://` resource), and
`TemplateWorkbookTests` guards it: headers equal `SectionImportColumns`, the sample rows plan with no problems,
the code and time columns are Text, the Instructions sheet exists and still holds the text of the instructions file.
Change the column list or the instructions and the test fails until the script is updated and re-run.

The file name has no spaces on purpose. The Save dialog will suggest "TermPoint Sections Template.xlsx".
