using System.Collections.Generic;

namespace SchedulingAssistant.ViewModels.Management;

/// <summary>
/// A single workflow entry: a user-facing scenario with expandable solution steps.
/// </summary>
public sealed class WorkflowItem
{
    /// <summary>Short label shown as the card title (e.g. "Assign Workload").</summary>
    public required string Title { get; init; }

    /// <summary>
    /// One-sentence user story describing the problem or goal, shown prominently
    /// on the card face (e.g. "I need to assign instructors without conflicts").
    /// </summary>
    public required string UserStory { get; init; }

    /// <summary>
    /// Step-by-step solution text revealed when the card is expanded.
    /// Use newlines to separate steps.
    /// </summary>
    public required string Solution { get; init; }

    /// <summary>
    /// Hex color string for the card background (e.g. "#DBEAFE").
    /// Parsed by the view via a converter or direct binding.
    /// </summary>
    public required string CardColor { get; init; }
}

/// <summary>
/// ViewModel for the Workflows flyout. Exposes a static list of
/// <see cref="WorkflowItem"/> cards — each pairs a user story with
/// step-by-step guidance on how to accomplish it in TermPoint.
/// </summary>
public sealed class WorkflowsViewModel : ViewModelBase
{
    /// <summary>All workflow cards, displayed in a wrapping card layout.</summary>
    public IReadOnlyList<WorkflowItem> Workflows { get; } = BuildWorkflows();

    private static IReadOnlyList<WorkflowItem> BuildWorkflows() =>
    [
        new()
        {
            Title     = "I need to fill a room",
            UserStory = "The registrar gives us rooms we have to fill as much as possible. ",
            CardColor = "#DBEAFE",
            Solution  =
                "1. Use the Room Filter and select 'unroomed'. Section meetings without rooms are shown.\n" +
                "2. Choose a room not yet full, and use the Room Overlay to see that room's bookings (in red)\n" +
                "3. Look for unroomed meetings which do not conflict with the chosen room\n" +
                "4. Right-click to assign the room.\n" +
                "5. Repeat until that room is full, move on to the next room."
        },
        new()
        {
            Title     = "Where and when to put a section?", 
            UserStory = "I have to balance the instructor's schedule, room availability and program considerations\",",
            CardColor = "#D1FAE5",
            Solution  =
                "1. Filter down to a relevant view, showing the sections the new section interacts with.\n" +
                "2. Overlay the instructor's schedule as decided so far\n" +
                "3. Add the section, and supply just the requirements you have of the meetings (at the minimum the number of them and their length). If you require more (say the days it must meet, or room types, add those). \n" +
                "4. Use the room tool to see what options are available, comparing the room proposals shown by the system with your program requirement and the instructor's schedule."                 
        },
        new()
        {
            Title     = "Staffing sections and assigning workload",
            UserStory = "I know (mostly) when my sections will be offered. Now I need to assign instructors, and fill their workload",
            CardColor = "#FEF3C7",
            Solution  =
                "1. Pick an instructor whose workload is not yet full (consult workload view to see this) Overlay their schedule as it is know so far (scheduled commitment other than workload can be entered under 'Instructors')\n" +
                "2. Filter using the Instructor filter to see unstaffed sections (either isolated or emphasized)\n" +
                "3. Optionally, combine with other filters to eliminate irrelevant clutter (the instructor might teach only certain courses, or course with certain tags)\n" +
                "4. Find a course which fits the instructor's schedule. Right-click to assign it to the instructor\n" +
                "5. Continue until that instructor's workload is full. Move on to the next instructor."
        },
        new()
        {
            Title     = "Import a Shared Schedule",
            UserStory = "I need to see another department's sections on my grid to make sure our sections do not conflict.",
            CardColor = "#F5F0FF",
            Solution  =
                "1. Ask the other department to export their schedule from Sharing → Export. Encourage them to share a filtered view, one that shows just the relevant sections\n" +
                "2. They send you the CSV file or place it on a shared drive\n" +
                "3. Open Sharing → Import and select the CSV file.\n" +
                "4. Shared sections appear on the grid as translucent overlay blocks.\n" +
                "5. Use these overlays to avoid double-booking rooms or instructor conflicts."
        },
        new()
        {
            Title     = "Find an Open Room",
            UserStory = "I need to find a room that is available at a specific time.",
            CardColor = "#FFE4E6",
            Solution  =
                "1. Open the filter bar and select a Room from the Room dropdown.\n" +
                "2. The grid shows only sections assigned to that room — gaps are open slots.\n" +
                "3. To compare multiple rooms, clear the filter and try another room.\n" +
                "4. Alternatively, use the Room Browser (if available) for a side-by-side room availability view."
        },
        new()
        {
            Title     = "Instructor's schedules",
            UserStory = "I need a way to see the non-teaching schedules for instructors",
            CardColor = "#FFE4E6",
            Solution  =
                "1. n" +
                "2. \n" +
                "3. \n" +
                "4. "
        },
        new()
        {
            Title     = "The timetable is complicated. It's easy to miss conflicts",
            UserStory = "I need a way to ensure that our students are offered conflict-free schedules",
            CardColor = "#FFE4E6",
            Solution  =
                "1. Filters are your friend!\n" +
                "2. If you have certain courses that together form a program, tag them. \n" +
                "3. You can tag individual sections, but more effectively, assign tags to the courses in the course editor\n" +
                "4. Now every new section of those courses will get those tags.\n" +
                "5. When you're timetabling, filter to that tag (possibly with other filters), and ensure the sections together form workable schedules"
        },
    ];
}
