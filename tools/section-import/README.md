# Section import template

`make_template.py` builds the workbook that department administrators fill in for **Files, Import Sections…**:

    src/TermPoint/Assets/Templates/TermPointSectionsTemplate.xlsx

It has two sheets:

- **Sections**: the 18 template columns, a few sample rows showing every pattern (two instructors, an odd-week lab,
  one section on two rows, an online section), and Text formatting on Course Number, Section Code, Begin and End so
  Excel keeps leading zeros.
- **Instructions**: the text of `instructions.txt`, one paragraph per row. Markup: a line starting `# ` is a
  heading, `- ` is a bullet, and blank lines separate paragraphs.

## Run it

    python tools/section-import/make_template.py

It needs openpyxl (`python -m pip install --user openpyxl`). Run it again after editing `instructions.txt`, the
sample rows or the column list. The output is reproducible: unchanged inputs give identical bytes.

## Keep in step with the app

The column list in the script (`COLUMNS`) must match `SectionImportColumns.All` in the app, spelled and ordered
identically. The generated `.xlsx` is checked in (the app loads it as an `avares://` resource), and
`TemplateWorkbookTests` guards it: headers equal `SectionImportColumns`, the sample rows plan with no problems,
the code and time columns are Text, and the Instructions sheet exists. Change the column list and the test fails
until the script is updated and re-run.

The file name has no spaces on purpose. The Save dialog will suggest "TermPoint Sections Template.xlsx".
