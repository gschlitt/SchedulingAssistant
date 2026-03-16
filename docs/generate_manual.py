"""
Generates the SchedulingAssistant Maintenance Manual as a PDF.
Run: python generate_manual.py
Output: SchedulingAssistant_Maintenance_Manual.pdf (same directory)
"""

from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib import colors
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle,
    HRFlowable, PageBreak, KeepTogether
)
from reportlab.lib.enums import TA_LEFT, TA_CENTER

OUTPUT = "SchedulingAssistant_Maintenance_Manual.pdf"

# ──────────────────────────────────────────────
# Styles
# ──────────────────────────────────────────────

base_styles = getSampleStyleSheet()

def make_styles():
    s = {}
    s['title'] = ParagraphStyle(
        'ManualTitle',
        parent=base_styles['Title'],
        fontSize=26,
        spaceAfter=6,
        textColor=colors.HexColor('#1a1a2e'),
    )
    s['subtitle'] = ParagraphStyle(
        'ManualSubtitle',
        parent=base_styles['Normal'],
        fontSize=13,
        spaceAfter=4,
        textColor=colors.HexColor('#4a4a6a'),
        alignment=TA_CENTER,
    )
    s['date'] = ParagraphStyle(
        'Date',
        parent=base_styles['Normal'],
        fontSize=10,
        spaceBefore=2,
        spaceAfter=20,
        textColor=colors.HexColor('#888888'),
        alignment=TA_CENTER,
    )
    s['h1'] = ParagraphStyle(
        'H1',
        parent=base_styles['Heading1'],
        fontSize=16,
        spaceBefore=18,
        spaceAfter=6,
        textColor=colors.HexColor('#1a1a2e'),
        borderPad=4,
    )
    s['h2'] = ParagraphStyle(
        'H2',
        parent=base_styles['Heading2'],
        fontSize=13,
        spaceBefore=14,
        spaceAfter=4,
        textColor=colors.HexColor('#2d2d5a'),
    )
    s['h3'] = ParagraphStyle(
        'H3',
        parent=base_styles['Heading3'],
        fontSize=11,
        spaceBefore=10,
        spaceAfter=3,
        textColor=colors.HexColor('#3a3a7a'),
    )
    s['body'] = ParagraphStyle(
        'Body',
        parent=base_styles['Normal'],
        fontSize=10,
        leading=15,
        spaceBefore=2,
        spaceAfter=4,
    )
    s['bullet'] = ParagraphStyle(
        'Bullet',
        parent=base_styles['Normal'],
        fontSize=10,
        leading=14,
        leftIndent=18,
        bulletIndent=6,
        spaceAfter=2,
    )
    s['bullet2'] = ParagraphStyle(
        'Bullet2',
        parent=base_styles['Normal'],
        fontSize=10,
        leading=14,
        leftIndent=36,
        bulletIndent=24,
        spaceAfter=2,
    )
    s['code'] = ParagraphStyle(
        'Code',
        parent=base_styles['Code'],
        fontSize=8,
        leading=12,
        leftIndent=12,
        rightIndent=12,
        spaceBefore=4,
        spaceAfter=6,
        backColor=colors.HexColor('#f4f4f8'),
        textColor=colors.HexColor('#2a2a5a'),
        fontName='Courier',
        borderPad=6,
        borderRadius=3,
    )
    s['note'] = ParagraphStyle(
        'Note',
        parent=base_styles['Normal'],
        fontSize=9,
        leading=13,
        leftIndent=12,
        rightIndent=12,
        spaceBefore=4,
        spaceAfter=6,
        backColor=colors.HexColor('#fffbe6'),
        textColor=colors.HexColor('#5a4a00'),
        borderPad=5,
    )
    s['toc_entry'] = ParagraphStyle(
        'TocEntry',
        parent=base_styles['Normal'],
        fontSize=10,
        leading=16,
        leftIndent=0,
    )
    s['toc_entry2'] = ParagraphStyle(
        'TocEntry2',
        parent=base_styles['Normal'],
        fontSize=10,
        leading=16,
        leftIndent=16,
        textColor=colors.HexColor('#444444'),
    )
    return s

S = make_styles()

def H1(text): return Paragraph(text, S['h1'])
def H2(text): return Paragraph(text, S['h2'])
def H3(text): return Paragraph(text, S['h3'])
def P(text):  return Paragraph(text, S['body'])
def B(text):  return Paragraph(f"&#8226; {text}", S['bullet'])
def B2(text): return Paragraph(f"&#8226; {text}", S['bullet2'])
def Code(text):
    escaped = text.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
    lines = escaped.split('\n')
    formatted = '<br/>'.join(lines)
    return Paragraph(f'<font name="Courier" size="8">{formatted}</font>', S['code'])
def Note(text): return Paragraph(f"<b>Note:</b> {text}", S['note'])
def HR(): return HRFlowable(width="100%", thickness=0.5, color=colors.HexColor('#ccccdd'), spaceAfter=4)
def SP(n=6): return Spacer(1, n)

def two_col_table(rows, col_widths=None):
    if col_widths is None:
        col_widths = [2.2*inch, 4.3*inch]
    data = [[Paragraph(f"<b>{k}</b>", S['body']), Paragraph(v, S['body'])] for k, v in rows]
    t = Table(data, colWidths=col_widths)
    t.setStyle(TableStyle([
        ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('GRID', (0,0), (-1,-1), 0.4, colors.HexColor('#ddddee')),
        ('ROWBACKGROUNDS', (0,0), (-1,-1), [colors.white, colors.HexColor('#f8f8fc')]),
        ('LEFTPADDING', (0,0), (-1,-1), 6),
        ('RIGHTPADDING', (0,0), (-1,-1), 6),
        ('TOPPADDING', (0,0), (-1,-1), 4),
        ('BOTTOMPADDING', (0,0), (-1,-1), 4),
    ]))
    return t

def header_table(headers, rows, col_widths=None):
    if col_widths is None:
        col_widths = [1.5*inch, 1.8*inch, 3.2*inch]
    header_row = [Paragraph(f"<b>{h}</b>", S['body']) for h in headers]
    data_rows = [[Paragraph(c, S['body']) for c in row] for row in rows]
    t = Table([header_row] + data_rows, colWidths=col_widths)
    t.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), colors.HexColor('#2d2d5a')),
        ('TEXTCOLOR', (0,0), (-1,0), colors.white),
        ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('GRID', (0,0), (-1,-1), 0.4, colors.HexColor('#ddddee')),
        ('ROWBACKGROUNDS', (0,1), (-1,-1), [colors.white, colors.HexColor('#f8f8fc')]),
        ('LEFTPADDING', (0,0), (-1,-1), 6),
        ('RIGHTPADDING', (0,0), (-1,-1), 6),
        ('TOPPADDING', (0,0), (-1,-1), 4),
        ('BOTTOMPADDING', (0,0), (-1,-1), 4),
    ]))
    return t

# ──────────────────────────────────────────────
# Page template with header/footer
# ──────────────────────────────────────────────

def on_page(canvas, doc):
    canvas.saveState()
    w, h = letter
    # Footer
    canvas.setFont('Helvetica', 8)
    canvas.setFillColor(colors.HexColor('#888888'))
    canvas.drawString(inch, 0.5*inch, "SchedulingAssistant — Maintenance Manual")
    canvas.drawRightString(w - inch, 0.5*inch, f"Page {doc.page}")
    # Top rule
    canvas.setStrokeColor(colors.HexColor('#ccccdd'))
    canvas.setLineWidth(0.5)
    canvas.line(inch, h - 0.6*inch, w - inch, h - 0.6*inch)
    canvas.restoreState()

# ──────────────────────────────────────────────
# Document sections
# ──────────────────────────────────────────────

def cover_page():
    return [
        SP(80),
        Paragraph("SchedulingAssistant", S['title']),
        Paragraph("Maintenance &amp; Developer Manual", S['subtitle']),
        HR(),
        Paragraph("March 2026", S['date']),
        SP(20),
        P("This manual provides a medium-level technical overview of the SchedulingAssistant "
          "application for developers taking over maintenance of the codebase. It covers the "
          "application's purpose, architecture, data flow, key classes, and conventions used "
          "throughout the project."),
        PageBreak(),
    ]


def section_what_is():
    return [
        H1("1. What Is SchedulingAssistant?"),
        P("SchedulingAssistant is a <b>scheduling visualization and information management tool</b> "
          "for university administrators. It is <i>not</i> an auto-scheduler — it helps "
          "administrators see and manage how course sections fit together across a week."),
        SP(4),
        P("Key capabilities:"),
        B("Weekly calendar grid (Mon–Fri, time of day) showing all course sections as visual tiles"),
        B("Section list panel with inline editing, copy, and sort"),
        B("Workload panel summarizing instructor hours across semesters"),
        B("Multi-semester view with color-coded banners"),
        B("Management flyouts for Courses, Instructors, Rooms, Semesters, Settings, and more"),
        B("PNG export of the schedule grid and workload email mailer"),
        SP(4),
        two_col_table([
            ("Platform", "Avalonia UI (C# / .NET 8), targets Windows and macOS"),
            ("Distribution", "Self-contained executable — no .NET runtime needed on end-user machines"),
            ("Database", "SQLite, single local file chosen by user on first run"),
            ("UI Library", "Avalonia 11, Fluent theme, Inter font"),
            ("MVVM toolkit", "CommunityToolkit.Mvvm 8 (source generators)"),
        ]),
        SP(8),
    ]


def section_project_structure():
    return [
        H1("2. Project Structure"),
        Code(
            "SchedulingAssistant/\n"
            "  src/\n"
            "    SchedulingAssistant/           <- Main application project\n"
            "      App.axaml / App.axaml.cs     <- App startup & DI container\n"
            "      MainWindow.axaml / .cs        <- Root window & nav orchestration\n"
            "      ViewLocator.cs               <- Convention-based VM-to-View lookup\n"
            "      Constants.cs                 <- Domain constants\n"
            "      AppColors.axaml              <- Centralized color palette\n"
            "      Models/                      <- Domain entities (POCOs)\n"
            "      Data/                        <- Database layer\n"
            "        DatabaseContext.cs\n"
            "        Repositories/              <- 17 repository classes\n"
            "        SeedData.cs\n"
            "        JsonHelpers.cs\n"
            "      Services/                    <- Shared business logic\n"
            "      ViewModels/                  <- MVVM layer (~70 classes)\n"
            "        Management/                <- Flyout ViewModels (transient)\n"
            "        GridView/                  <- Schedule grid ViewModels\n"
            "      Views/                       <- XAML markup (~30 files)\n"
            "        Management/\n"
            "        GridView/\n"
            "      Converters/                  <- XAML value converters\n"
            "      Behaviors/                   <- Avalonia attached behaviors\n"
            "      Controls/                    <- Custom reusable controls\n"
            "    SchedulingAssistant.Tests/     <- Unit test project\n"
            "  docs/                            <- Documentation\n"
            "  .claude/                         <- Claude Code AI metadata"
        ),
        SP(8),
    ]


def section_startup():
    return [
        H1("3. Application Startup &amp; DI Container"),
        P("Startup is async and split across two files:"),
        SP(4),
        H2("3.1  App.axaml.cs"),
        P("<b>OnFrameworkInitializationCompleted()</b> creates a hidden MainWindow and shows it. "
          "The actual initialization work happens when the window raises its <b>Opened</b> event."),
        P("<b>InitializeServices(dbPath)</b> builds the DI container "
          "(Microsoft.Extensions.DependencyInjection). It is called once the database path is known."),
        SP(4),
        H2("3.2  MainWindow.axaml.cs — OnOpened()"),
        P("The async startup sequence runs in this order:"),
        B("Show splash screen"),
        B("Prompt for database location (DatabaseLocationDialog) if no path is saved in AppSettings"),
        B("Call App.InitializeServices(dbPath) — builds the DI container"),
        B("DatabaseContext auto-creates schema &amp; runs migrations"),
        B("SeedData.EnsureSeeded() seeds defaults (legal start times, block patterns, property types)"),
        B("SemesterContext.Reload() restores last-selected academic year &amp; semesters from AppSettings"),
        B("SectionStore.Reload() populates the in-memory section cache"),
        B("Set MainWindow.DataContext = MainWindowViewModel (resolved from DI)"),
        B("Show window, dismiss splash"),
        SP(6),
        H2("3.3  DI Registration Summary"),
        P("Registrations are split into <b>singletons</b> (one shared instance) and "
          "<b>transients</b> (new instance per resolution):"),
        header_table(
            ["Lifetime", "Class", "Notes"],
            [
                ["Singleton", "DatabaseContext", "Opened SQLite connection + schema owner"],
                ["Singleton", "SemesterContext", "AY &amp; semester selector; multi-semester state"],
                ["Singleton", "SectionStore", "Section cache + selection event hub"],
                ["Singleton", "SectionChangeNotifier", "Event bridge for data-change notifications"],
                ["Singleton", "MainWindowViewModel", "Root VM; flyout orchestration"],
                ["Singleton", "SectionListViewModel", "Left-panel section list"],
                ["Singleton", "ScheduleGridViewModel", "Right-panel schedule grid"],
                ["Singleton", "WorkloadPanelViewModel", "Bottom-left workload panel"],
                ["Transient", "All Repositories (17)", "New instance per resolution"],
                ["Transient", "All management flyout VMs", "e.g. InstructorListViewModel"],
                ["Transient", "DialogService", "File dialogs, confirmations"],
                ["Transient", "ScheduleValidationService", "Copy Semester conflict checks"],
                ["Transient", "ExportViewModel, WorkloadMailerViewModel, …", "Flyout-specific VMs"],
            ],
            col_widths=[1.0*inch, 2.2*inch, 3.3*inch]
        ),
        SP(8),
    ]


def section_database():
    return [
        H1("4. Database Layer"),
        H2("4.1  Schema Philosophy"),
        P("All tables follow a <b>hybrid document-store pattern</b>:"),
        B("Stable identity and foreign-key columns provide SQL-queryable structure (e.g. "
          "<b>id</b>, <b>semester_id</b>, <b>course_id</b>)"),
        B("Human-readable denormalized columns (e.g. <b>section_code</b>, <b>course_code</b>) "
          "are included for database browsing, even though the app reads them from the JSON column"),
        B("A <b>data TEXT</b> column stores all other fields as a JSON object"),
        SP(4),
        P("Example — Sections table:"),
        Code(
            "CREATE TABLE Sections (\n"
            "  id           TEXT PRIMARY KEY,\n"
            "  semester_id  TEXT NOT NULL,\n"
            "  course_id    TEXT,\n"
            "  section_code TEXT,           -- denormalized from JSON\n"
            "  course_code  TEXT,           -- denormalized from JSON\n"
            "  data         TEXT NOT NULL DEFAULT '{}'\n"
            ");"
        ),
        Note("The app does not use foreign key enforcement. Referential integrity is maintained "
             "by the application layer."),
        SP(4),
        H2("4.2  DatabaseContext"),
        P("<b>File:</b> Data/DatabaseContext.cs"),
        P("Singleton that owns the open SQLite connection. Responsibilities:"),
        B("<b>InitializeSchema()</b> — creates all tables on a new database"),
        B("<b>Migrate()</b> — runs on every startup; uses PRAGMA table_info() to detect missing "
          "columns and ALTER TABLE to add them; backfills readable columns from JSON for old rows"),
        B("<b>EnsureSeedData()</b> — delegates to SeedData.EnsureSeeded()"),
        SP(4),
        H2("4.3  Repository Pattern"),
        P("Each entity has a corresponding repository class in <b>Data/Repositories/</b>. "
          "All repositories are <b>transient</b> and receive DatabaseContext via constructor injection."),
        P("Key repository methods:"),
        header_table(
            ["Repository", "Notable Method", "Description"],
            [
                ["SectionRepository", "ExistsBySectionCode()", "Uniqueness check within course+semester"],
                ["SectionRepository", "GetByCourseId()", "All sections for a course, across all semesters"],
                ["SectionRepository", "DeleteBySemester()", "Bulk delete used by Empty Semester feature"],
                ["SectionRepository", "Insert(tx)", "Accepts optional SqliteTransaction (Copy Semester)"],
                ["LegalStartTimeRepository", "GetByAcademicYear()", "Block lengths for a given AY"],
                ["SectionPrefixRepository", "GetAll()", "All prefixes; used by prefix picker in editor"],
            ],
            col_widths=[1.8*inch, 2.0*inch, 2.7*inch]
        ),
        SP(4),
        P("Query conventions:"),
        B("All queries use parameterized SQL with <b>$paramName</b> syntax — no string concatenation"),
        B("JSON fields are accessed via SQLite's <b>json_extract(data, '$.field')</b> or "
          "the <b>-&gt;&gt;</b> operator"),
        B("Transactional operations pass a <b>SqliteTransaction</b> parameter (e.g. Copy Semester "
          "wraps all inserts in one transaction)"),
        SP(8),
    ]


def section_models():
    return [
        H1("5. Domain Models"),
        P("All models are plain C# classes (POCOs) in the <b>Models/</b> folder. They are "
          "serialized to/from the <b>data</b> JSON column via System.Text.Json."),
        SP(4),
        header_table(
            ["Model", "Key Fields", "Notes"],
            [
                ["Section", "SemesterId, CourseId, SectionCode, Schedule, InstructorAssignments, Tags, …",
                 "Core entity; schedule is List&lt;SectionDaySchedule&gt;"],
                ["Instructor", "FirstName, LastName, Email, IsActive, StaffTypeId",
                 "IsActive flag used to filter display lists"],
                ["Course", "CalendarCode, Title, SubjectId",
                 "Subject groups courses by discipline"],
                ["Semester", "Name, AcademicYearId",
                 "e.g. 'Fall 2024'; belongs to an AcademicYear"],
                ["AcademicYear", "Name",
                 "e.g. '2024-2025'; container for semesters"],
                ["Room", "Building, RoomNumber",
                 "Physical location for section meetings"],
                ["SectionDaySchedule", "Day, StartMinutes, BlockLength",
                 "Single meeting instance (day + time)"],
                ["InstructorAssignment", "InstructorId, Workload",
                 "Multi-instructor; workload is nullable decimal"],
                ["InstructorCommitment", "InstructorId, SemesterId, CommittedWorkload",
                 "Target workload per semester"],
                ["SectionPrefix", "Prefix, CampusId",
                 "e.g. 'AB' → Abbotsford campus; drives auto-code generation"],
                ["LegalStartTime", "AcademicYearId, BlockLength, StartMinutes",
                 "Composite PK; defines valid start times per block length per AY"],
                ["SectionPropertyValue", "Type, Name, Color",
                 "Tags, SectionTypes, MeetingTypes, Campuses, etc."],
                ["Release", "InstructorId, SemesterId, Hours, Description",
                 "Teaching release reducing workload total"],
            ],
            col_widths=[1.5*inch, 2.8*inch, 2.2*inch]
        ),
        SP(4),
        P("The <b>SectionPropertyTypes</b> constants class defines the valid type strings: "
          "<i>sectionType, meetingType, staffType, campus, tag, resource, reserve</i>."),
        SP(8),
    ]


def section_services():
    return [
        H1("6. Services Layer"),
        H2("6.1  SectionStore (Singleton)"),
        P("<b>File:</b> Services/SectionStore.cs"),
        P("The single source of truth for section data and cross-view selection. All "
          "ViewModels that display sections subscribe to this instead of querying the "
          "database directly."),
        SP(4),
        two_col_table([
            ("SectionsBySemester", "IReadOnlyDictionary&lt;string, IReadOnlyList&lt;Section&gt;&gt; — "
             "cached sections grouped by semester ID"),
            ("Reload(repo, semIds)", "Queries DB for the given semester IDs, updates cache, "
             "fires SectionsChanged"),
            ("SetSelection(id?)", "Idempotent; fires SelectionChanged only if value changes"),
            ("SectionsChanged event", "Fired after every Reload(); subscribers: ScheduleGridVm, "
             "WorkloadPanelVm, CourseHistoryVm"),
            ("SelectionChanged event", "Fired by SetSelection(); subscribers: SectionListVm, "
             "ScheduleGridVm"),
        ]),
        SP(4),
        P("SectionListViewModel is the <b>driver</b> — it calls Reload() after every save/delete "
          "and on semester change. All other ViewModels are <b>passive subscribers</b>."),
        SP(6),
        H2("6.2  SemesterContext (Singleton)"),
        P("<b>File:</b> Services/SemesterContext.cs"),
        P("Manages which academic year and which semester(s) are currently active. "
          "Supports both single-semester and multi-semester (checkbox) mode. "
          "Persists selection to AppSettings on change and restores it on startup."),
        SP(6),
        H2("6.3  SectionChangeNotifier (Singleton)"),
        P("<b>File:</b> Services/SectionChangeNotifier.cs"),
        P("An event bridge used exclusively for <b>commitment CRUD</b> → ScheduleGridViewModel reload. "
          "Allows CommitmentsManagementViewModel (transient) to notify ScheduleGridViewModel "
          "(singleton) without a direct reference, avoiding circular DI dependencies."),
        SP(6),
        H2("6.4  AppSettings"),
        P("<b>File:</b> Services/AppSettings.cs"),
        P("Persists user preferences to <b>%AppData%/SchedulingAssistant/settings.json</b>. "
          "Loaded at startup; saved whenever state changes."),
        P("Stored values include: database path, last selected academic year &amp; semester IDs, "
          "last export path, workload mailer subject/body templates, section sort mode, "
          "Saturday visibility toggle, ThrowOnError debug toggle."),
        SP(6),
        H2("6.5  SectionPrefixHelper (Static Utility)"),
        P("<b>File:</b> Services/SectionPrefixHelper.cs"),
        P("Shared by the section editor and the Copy Section command:"),
        two_col_table([
            ("MatchPrefix(code, prefixes)", "Returns the longest matching prefix where "
             "the next character is not a digit"),
            ("FindNextAvailableCode(prefix, existing)", "Gap-fills numeric suffixes 1..999 "
             "to find the next available code (e.g. AB1, AB2, …)"),
            ("AdvanceSectionCode(code, prefixes, existing)",
             "For Copy: if a prefix is matched, gap-fills; otherwise increments trailing integer"),
        ]),
        SP(6),
        H2("6.6  ScheduleValidationService (Transient)"),
        P("<b>File:</b> Services/ScheduleValidationService.cs"),
        P("<b>FindIncompatibleSections(sections, targetAcademicYearId, legalStartTimesRepo)</b> — "
          "checks whether each section's meeting times (block length + start time) exist in the "
          "target academic year's legal start time matrix. Returns a list of incompatible sections "
          "with reasons. Used by Copy Semester's FlaggedWarning state."),
        SP(8),
    ]


def section_viewmodels():
    return [
        H1("7. ViewModels"),
        H2("7.1  MVVM Conventions"),
        B("All ViewModels inherit <b>ViewModelBase</b> which inherits <b>ObservableObject</b> "
          "from CommunityToolkit.Mvvm"),
        B("<b>[ObservableProperty]</b> on a private field auto-generates a public property "
          "with PropertyChanged notification"),
        B("<b>[RelayCommand]</b> auto-generates a RelayCommand instance from a method"),
        B("Partial methods <b>OnXxxChanged()</b> and <b>OnXxxChanging()</b> inject custom "
          "logic on property changes without breaking the source generator"),
        B("<b>x:CompileBindings=\"False\"</b> is set on all views — compiled bindings are "
          "disabled app-wide to avoid Avalonia's binding re-fire lifecycle corrupting "
          "step-gate state machines"),
        SP(6),
        H2("7.2  Root (Singleton) ViewModels"),
        SP(2),
        H3("MainWindowViewModel"),
        P("<b>File:</b> ViewModels/MainWindowViewModel.cs"),
        P("Root ViewModel for the main window. Owns references to the three permanent panel "
          "ViewModels (SectionListVm, ScheduleGridVm, WorkloadPanelVm). Manages the flyout "
          "overlay via <b>FlyoutPage</b> and <b>FlyoutTitle</b> observable properties. "
          "Provides all menu commands (Navigate to Instructors, Courses, Rooms, Settings, "
          "Export, etc.)."),
        SP(4),
        H3("SectionListViewModel"),
        P("<b>File:</b> ViewModels/Management/SectionListViewModel.cs"),
        P("Drives the left-panel section list. Responsibilities:"),
        B("Loads sections from SectionStore and groups them by semester (with color banners "
          "in multi-semester mode)"),
        B("Manages inline editor state: one SectionEditViewModel open at a time"),
        B("Calls SectionStore.Reload() after every save/delete — making it the data driver"),
        B("Supports three sort modes (SubjectCourseCode, Instructor, SectionType); "
          "preference persisted in AppSettings"),
        B("Add, Edit, Copy, Delete commands; Copy uses SectionPrefixHelper.AdvanceSectionCode"),
        SP(4),
        H3("ScheduleGridViewModel"),
        P("<b>File:</b> ViewModels/GridView/ScheduleGridViewModel.cs"),
        P("Drives the right-panel weekly calendar grid. Responsibilities:"),
        B("Subscribes to SectionStore.SectionsChanged to rebuild the grid on data changes"),
        B("Applies multi-level filtering (by instructor, room, department, campus, tag, type)"),
        B("Detects overlapping sections (different time spans on same day → side-by-side) "
          "and co-scheduled sections (identical time span → stacked in one tile)"),
        B("Manages right-click context menu (quick-assign instructors, rooms, tags)"),
        B("<b>ExportToPng(path)</b> renders the full logical canvas to a PNG at 2× DPI (192 DPI)"),
        SP(4),
        H3("WorkloadPanelViewModel"),
        P("<b>File:</b> ViewModels/WorkloadPanelViewModel.cs"),
        P("Drives the bottom-left workload panel. Aggregates instructor hours across the "
          "currently selected semesters. Subscribes to SectionStore.SectionsChanged to "
          "auto-refresh. Displays workload rows grouped by instructor."),
        SP(6),
        H2("7.3  Section Editor — Step-Gate Pattern"),
        P("<b>File:</b> ViewModels/Management/SectionEditViewModel.cs"),
        P("The inline section editor enforces a strict two-step unlock sequence:"),
        B("<b>Step 1:</b> Course must be selected. Section Code field is disabled "
          "(IsSectionCodeEnabled = false) until a course is chosen."),
        B("<b>Step 2:</b> Section Code must be entered and validated. All other fields are "
          "disabled (AreOtherFieldsEnabled = false) until the Section Code field loses "
          "focus and CommitSectionCode() passes the uniqueness check."),
        SP(4),
        P("<b>AreOtherFieldsEnabled</b> is a purely computed property — no mutable flag. "
          "It returns true only when ALL of these hold simultaneously:"),
        B2("SelectedCourseId is non-empty"),
        B2("SectionCode.Trim() is non-empty"),
        B2("SectionCodeError is null"),
        B2("_validatedCourseId == SelectedCourseId (snapshot match)"),
        B2("_validatedSectionCode == SectionCode.Trim() (case-insensitive snapshot match)"),
        SP(4),
        P("The snapshot (_validatedCourseId, _validatedSectionCode) is set only by "
          "CommitSectionCode() on success, or by the constructor for sections that already "
          "have both fields. Any change to course or code clears SectionCodeError and "
          "re-evaluates the computed property — so the step-gate is always consistent, "
          "even across Avalonia's binding re-fire lifecycle."),
        SP(4),
        Note("The Section Code TextBox lives inside a DataTemplate and cannot be "
             "reached directly. SectionListView.axaml.cs registers a bubbled LostFocusEvent "
             "handler on the UserControl and forwards it to EditVm.CommitSectionCode() when "
             "the source control's name is 'SectionCodeBox'."),
        SP(6),
        H2("7.4  Significant Management Flyout ViewModels"),
        header_table(
            ["ViewModel", "Flyout", "Key Behaviour"],
            [
                ["CourseListViewModel", "Courses", "Two-column layout: CRUD left, CourseHistoryViewModel right (AY → Semester → Sections hierarchy)"],
                ["InstructorListViewModel", "Instructors", "CRUD + workload summary, releases, commitments assignment"],
                ["CopySemesterViewModel", "Copy Semester", "Three-state machine: Ready → FlaggedWarning → Complete; uses ScheduleValidationService; transaction-safe inserts"],
                ["EmptySemesterViewModel", "Empty Semester", "Confirmation-based; calls SectionRepository.DeleteBySemester()"],
                ["WorkloadMailerViewModel", "Export &gt; Mailer", "Three-state: Setup → Sending → Done; builds mailto: URIs with template placeholders"],
                ["ExportViewModel", "Export &gt; PNG", "File save dialog + calls ScheduleGridView.ExportToPng()"],
                ["SectionPrefixListViewModel", "Settings &gt; Prefixes", "CRUD for prefixes; prefix must not end in a digit"],
                ["CommitmentsManagementViewModel", "Instructors", "Commitment CRUD; calls SectionChangeNotifier after save"],
                ["LegalStartTimeListViewModel", "Semesters &gt; Legal Start Times", "Per-AY block-length / start-time matrix editor"],
                ["DebugTestDataViewModel", "Debug", "Generates random test sections; DEBUG builds only"],
            ],
            col_widths=[1.8*inch, 1.3*inch, 3.4*inch]
        ),
        SP(8),
    ]


def section_navigation():
    return [
        H1("8. Navigation &amp; Flyout System"),
        H2("8.1  Three-Panel Layout"),
        P("MainWindow uses a <b>ThreePanelGrid</b> (ColumnDefinitions=\"220,4,*\"). "
          "When the inline section editor is open, the left column widens to 500px."),
        B("Left top — DetachablePanel 'Sections' → SectionListView"),
        B("Left bottom — DetachablePanel 'Workload' → WorkloadPanelView"),
        B("Right — DetachablePanel 'Schedule Grid' → ScheduleGridView"),
        SP(4),
        H2("8.2  Flyout Overlay"),
        P("Management screens open as a <b>drop-from-top overlay</b> with a dimmed backdrop. "
          "The flyout is not a separate window — it is a layer within MainWindow."),
        B("<b>MainWindowViewModel.FlyoutPage</b> — holds the current management ViewModel; "
          "null means no flyout is open"),
        B("<b>FlyoutTitle</b> — header text shown in the flyout"),
        B("Navigation methods call <b>OpenFlyout&lt;TViewModel&gt;(title)</b>, resolving "
          "the ViewModel from DI"),
        B("Pressing Escape or clicking the Close button calls <b>CloseFlyout()</b>"),
        SP(4),
        H2("8.3  ViewLocator"),
        P("<b>File:</b> ViewLocator.cs"),
        P("Convention-based ViewModel → View resolution. Given a ViewModel type, it "
          "strips 'ViewModel' from the class name, swaps '.ViewModels.' for '.Views.' "
          "in the namespace, and resolves the matching View type. Registered as Avalonia's "
          "IDataTemplate so it is invoked automatically by ContentControl bindings."),
        SP(4),
        H2("8.4  Database Switching"),
        P("The user can open a different database file via File &gt; Open. "
          "<b>MainWindow.SwitchDatabaseAsync(path)</b> re-runs the full initialization "
          "sequence with the new path — effectively restarting the app in-place."),
        SP(8),
    ]


def section_data_flow():
    return [
        H1("9. Data Flow"),
        H2("9.1  Section CRUD Flow"),
        Code(
            "User edits section in SectionEditViewModel\n"
            "  └─ CommitSectionCode() validates uniqueness via SectionRepository\n"
            "  └─ SaveCommand() calls SectionRepository.Insert() or .Update()\n"
            "       └─ SectionListViewModel.Load(selectSectionId)  (driver)\n"
            "            └─ SectionStore.Reload(repo, semesterIds)\n"
            "                 ├─ SectionStore.SectionsChanged fires\n"
            "                 │    ├─ ScheduleGridViewModel.ReloadCore()\n"
            "                 │    ├─ WorkloadPanelViewModel.Reload()\n"
            "                 │    └─ CourseHistoryViewModel.Reload()  (if open)\n"
            "                 └─ SectionStore.SelectionChanged fires  (if selection changed)\n"
            "                      ├─ SectionListViewModel highlights selected card\n"
            "                      └─ ScheduleGridViewModel highlights selected tile"
        ),
        SP(6),
        H2("9.2  Semester Change Flow"),
        Code(
            "User changes AY or semester selection in SemesterContext\n"
            "  └─ SemesterContext fires SelectionChanged\n"
            "       └─ SectionListViewModel.OnSemesterSelectionChanged()\n"
            "            └─ SectionStore.Reload(repo, newSemesterIds)\n"
            "                 └─ (same fan-out as Section CRUD above)"
        ),
        SP(6),
        H2("9.3  Right-Click Context Menu Flow (Schedule Grid)"),
        Code(
            "User right-clicks a section tile on ScheduleGridView\n"
            "  └─ Code-behind PrepareContextMenu() → SectionContextMenuViewModel.Load()\n"
            "       └─ Popup opens; user picks instructor/room/tag\n"
            "            └─ SectionContextMenuViewModel.Confirm()\n"
            "                 └─ SectionRepository.Update(section)\n"
            "                      └─ SectionChangeNotifier.NotifySectionChanged()\n"
            "                           ├─ SectionListViewModel reloads\n"
            "                           └─ ScheduleGridViewModel reloads"
        ),
        SP(6),
        H2("9.4  Copy Semester Flow"),
        Code(
            "CopySemesterViewModel.CopyCommand()\n"
            "  1. ScheduleValidationService.FindIncompatibleSections()\n"
            "     └─ If any → state = FlaggedWarning (user must Abort or Continue)\n"
            "  2. Open SqliteTransaction\n"
            "  3. For each section in source semester:\n"
            "       └─ Build new Section (copy fields per checkboxes)\n"
            "       └─ SectionRepository.Insert(section, transaction)\n"
            "  4. Commit transaction\n"
            "  5. State = Complete"
        ),
        SP(8),
    ]


def section_conventions():
    return [
        H1("10. Coding Conventions"),
        H2("10.1  MVVM"),
        B("ViewModels contain all business logic; Views are markup-only where possible"),
        B("Prefer attached behaviors (Behaviors/) over code-behind"),
        B("Code-behind is acceptable only for things that are unnatural in XAML "
          "(e.g. LostFocus forwarding, context menu wiring, PNG export)"),
        SP(4),
        H2("10.2  Styling &amp; Resources"),
        B("All colors reference <b>AppColors.axaml</b> via <b>{StaticResource}</b> — "
          "no hardcoded hex values in views"),
        B("All font sizes and font weights reference named resources in <b>App.axaml</b>"),
        B("Compact font sizes (10–11) in the section list editor to conserve vertical space"),
        SP(4),
        H2("10.3  Documentation"),
        B("Methods use <b>/// XML documentation comments</b> (triple-slash) for all "
          "public and significant internal members"),
        B("Comments explain purpose, parameters, return values, and any exceptions thrown"),
        B("Comments are liberal — the codebase may be maintained by developers "
          "unfamiliar with C# or Avalonia"),
        SP(4),
        H2("10.4  Logging"),
        P("Logs are written to <b>%AppData%/SchedulingAssistant/logs/</b> via "
          "<b>FileAppLogger</b>. Old log files are pruned on startup. "
          "The logger is available app-wide via <b>App.Logger</b>."),
        SP(4),
        H2("10.5  Error Handling"),
        P("A <b>ThrowOnError</b> debug toggle (Debug flyout) re-throws exceptions via "
          "ExceptionDispatchInfo after logging, so errors surface in the debugger during "
          "development. In production, errors are caught and logged silently."),
        SP(8),
    ]


def section_key_files():
    return [
        H1("11. Key Files Quick Reference"),
        header_table(
            ["File", "Path", "Why It Matters"],
            [
                ["App.axaml.cs", "src/SchedulingAssistant/", "DI container setup; InitializeServices()"],
                ["MainWindow.axaml.cs", "src/SchedulingAssistant/Views/", "Async startup sequence; database switching; context menu wiring"],
                ["MainWindowViewModel.cs", "ViewModels/", "Flyout orchestration; all menu commands"],
                ["DatabaseContext.cs", "Data/", "Schema creation, migrations, seeding"],
                ["SectionStore.cs", "Services/", "Section cache; cross-view selection hub"],
                ["SemesterContext.cs", "Services/", "AY + semester selection; multi-semester mode"],
                ["SectionListViewModel.cs", "ViewModels/Management/", "Section list driver; calls SectionStore.Reload()"],
                ["SectionEditViewModel.cs", "ViewModels/Management/", "Step-gate inline editor; snapshot pattern"],
                ["ScheduleGridViewModel.cs", "ViewModels/GridView/", "Grid rendering; filtering; PNG export"],
                ["SectionPrefixHelper.cs", "Services/", "Static code-gen utility; shared by editor + Copy"],
                ["CopySemesterViewModel.cs", "ViewModels/Management/", "Copy Semester state machine; transactional inserts"],
                ["AppSettings.cs", "Services/", "Persisted user preferences (JSON in AppData)"],
                ["AppColors.axaml", "src/SchedulingAssistant/", "Centralized color palette — edit here for theming"],
                ["ViewLocator.cs", "src/SchedulingAssistant/", "Convention-based VM → View resolution"],
                ["SeedData.cs", "Data/", "Default legal start times, block patterns, property types"],
            ],
            col_widths=[1.8*inch, 1.8*inch, 2.9*inch]
        ),
        SP(8),
    ]


def section_pending():
    return [
        H1("12. Known Pending Work"),
        P("The following items are known incomplete or require attention:"),
        SP(4),
        B("<b>Prune deleted section properties</b> — when a tag, section type, room, or "
          "other property value is deleted, it must be scrubbed from all sections that "
          "reference it across all semesters. Currently, stale references remain. "
          "Fix location: delete paths in SectionPropertiesViewModel (and any other "
          "place properties are deleted)."),
        B("<b>Pre-shipping cleanup</b> — remove debug logging from "
          "SectionListViewModel.GenerateRandomSections() and LoadCore(); remove "
          "#if DEBUG simulation blocks and Ctrl+Shift+E/W hotkeys in MainWindow."),
        SP(8),
    ]


def section_packages():
    return [
        H1("13. NuGet Packages &amp; Tooling"),
        header_table(
            ["Package", "Version", "Purpose"],
            [
                ["Avalonia", "11.3.12", "Core UI framework"],
                ["Avalonia.Controls.DataGrid", "11.3.12", "DataGrid control"],
                ["Avalonia.Desktop", "11.3.12", "Desktop platform support"],
                ["Avalonia.Themes.Fluent", "11.3.12", "Fluent design theme"],
                ["Avalonia.Fonts.Inter", "11.3.12", "Inter font family"],
                ["Avalonia.Svg.Skia", "11.3.12", "SVG icon rendering"],
                ["CommunityToolkit.Mvvm", "8.3.2", "ObservableObject, RelayCommand, [ObservableProperty]"],
                ["Microsoft.Extensions.DependencyInjection", "8.0.1", "DI container"],
                ["Microsoft.Data.Sqlite", "8.0.11", "SQLite driver"],
                ["HotAvalonia", "3.1.0", "Hot reload during development"],
                [".NET", "8.0", "Target framework (LTS)"],
            ],
            col_widths=[2.2*inch, 1.0*inch, 3.3*inch]
        ),
        SP(8),
    ]


def section_glossary():
    return [
        H1("Glossary"),
        two_col_table([
            ("AY", "Academic Year (e.g. 2024–2025)"),
            ("Section", "A course section: specific course offering in a semester with instructor(s), room, and schedule"),
            ("SectionStore", "Singleton in-memory cache of all sections for the selected semesters; the cross-view data and selection hub"),
            ("SemesterContext", "Singleton managing which AY and semester(s) are currently active"),
            ("SectionChangeNotifier", "Event bridge singleton — allows transient management VMs to notify singleton view VMs without direct references"),
            ("Step-gate", "The two-step unlock sequence in the section editor: Course → validated Section Code → all other fields"),
            ("Snapshot pattern", "AreOtherFieldsEnabled compares live values against a snapshot set by CommitSectionCode() to avoid needing a mutable flag"),
            ("Flyout", "A management screen that slides down over the main window as a modal-like overlay"),
            ("DetachablePanel", "Reusable panel container control; wraps left/bottom panels with a header and collapse/detach support"),
            ("ViewLocator", "Convention-based class that maps a ViewModel type to its matching View type for Avalonia's ContentControl"),
            ("Document store", "The JSON+identity-column schema pattern used for all database tables"),
            ("Legal Start Time", "A configured valid begin time for a given block length in a given academic year (e.g. 8:00 AM for 1.5h blocks)"),
            ("Block Pattern", "A meeting frequency template (e.g. MWF, TR) defining how many days per week a section meets"),
            ("Section Prefix", "A short code (e.g. AB, CH) optionally linked to a campus; drives auto-code generation in the section editor"),
            ("Workload", "The fractional teaching load assigned to an instructor for a section"),
        ]),
        SP(8),
    ]


# ──────────────────────────────────────────────
# Build
# ──────────────────────────────────────────────

def build():
    doc = SimpleDocTemplate(
        OUTPUT,
        pagesize=letter,
        leftMargin=inch,
        rightMargin=inch,
        topMargin=inch,
        bottomMargin=0.75*inch,
    )

    story = []
    story += cover_page()
    story += section_what_is()
    story += section_project_structure()
    story += section_startup()
    story += section_database()
    story += section_models()
    story += section_services()
    story += section_viewmodels()
    story += section_navigation()
    story += section_data_flow()
    story += section_conventions()
    story += section_key_files()
    story += section_pending()
    story += section_packages()
    story += section_glossary()

    doc.build(story, onFirstPage=on_page, onLaterPages=on_page)
    print(f"PDF written to: {OUTPUT}")


if __name__ == "__main__":
    build()
