using TermPoint.Services;
using TermPoint.ViewModels;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Tests of <see cref="UnsavedChangesGate"/>, the one "Save Changes?" question asked before a close, a switch or a
/// restore (undo design, story 5): when it is asked, what each answer does to the open editor, and its approved wording.
/// </summary>
public sealed class UnsavedChangesGateTests
{
    /// <summary>A fixed "last saved" time: 10:42.</summary>
    private static readonly DateTime LastSave = new(2026, 11, 26, 10, 42, 0);

    /// <summary>Unsaved changes, saved earlier this session unless told otherwise.</summary>
    private static UnsavedState Unsaved(string? card = null, bool savedThisSession = true)
        => new(true, LastSave, savedThisSession, card);

    /// <summary>Nothing unsaved (a card heading may still be present).</summary>
    private static UnsavedState Clean(string? card = null) => new(false, LastSave, true, card);

    /// <summary>Runs the gate for a close; the editor is found only while it has changes.</summary>
    private static Task<UnsavedChangesAnswer> Resolve(
        UnsavedState? state, FakeUnappliedEditor? editor, ScriptedPrompts prompts, Func<bool>? isWriter = null)
        => UnsavedChangesGate.ResolveAsync(
            HistoryText.CloseTitle, state, () => editor is { Has: true } ? editor : null, prompts, isWriter ?? (() => true));

    /// <summary>A script that expects the close question and answers it.</summary>
    private static ScriptedPrompts Asked(ThreeChoiceResult answer, string? body = null, Action? before = null)
        => new ScriptedPrompts().ExpectThreeChoice(HistoryText.CloseTitle, answer, body, before);

    // ── When it is asked ──────────────────────────────────────────────────────

    /// <summary>Not a writer: nothing is asked, even with unsaved changes and a dirty editor.</summary>
    [Fact]
    public async Task NotAWriter_AsksNothing()
    {
        var editor = new FakeUnappliedEditor();
        var prompts = new ScriptedPrompts();

        var answer = await Resolve(Unsaved(), editor, prompts, () => false);

        Assert.Equal(UnsavedChangesAnswer.NothingToAsk, answer);
        Assert.Empty(prompts.Calls);
        Assert.Equal(0, editor.ApplyCalls + editor.DiscardCalls);
    }

    /// <summary>Nothing unsaved and no editor with changes: nothing is asked (also for a null state).</summary>
    [Fact]
    public async Task NothingUnsaved_NoEditor_AsksNothing()
    {
        var prompts = new ScriptedPrompts();

        Assert.Equal(UnsavedChangesAnswer.NothingToAsk, await Resolve(Clean(), null, prompts));
        Assert.Equal(UnsavedChangesAnswer.NothingToAsk, await Resolve(null, null, prompts));
        Assert.Equal(UnsavedChangesAnswer.NothingToAsk, await Resolve(Clean(), new FakeUnappliedEditor { Has = false }, prompts));
        Assert.Empty(prompts.Calls);
    }

    /// <summary>Only unsaved changes: asked, with the three approved buttons and the unsaved body; Save is the answer.</summary>
    [Fact]
    public async Task UnsavedOnly_IsAsked_WithTheApprovedButtonsAndBody()
    {
        var prompts = Asked(ThreeChoiceResult.First, UnsavedChangesGate.Body(Unsaved(), false));

        var answer = await Resolve(Unsaved(), null, prompts);

        Assert.Equal(UnsavedChangesAnswer.Save, answer);
        var call = Assert.Single(prompts.Calls);
        Assert.Equal("Save|Don't Save|Cancel", call.Buttons);
        Assert.Equal(0, prompts.Remaining);
    }

    /// <summary>Only an editor with changes: asked.</summary>
    [Fact]
    public async Task EditorOnly_IsAsked()
    {
        var editor = new FakeUnappliedEditor();
        var prompts = Asked(ThreeChoiceResult.Third, UnsavedChangesGate.Body(Clean(), true));

        Assert.Equal(UnsavedChangesAnswer.Stop, await Resolve(Clean(), editor, prompts));

        Assert.Equal(0, prompts.Remaining);
    }

    /// <summary>Both unsaved changes and an editor: asked once, with the body naming both.</summary>
    [Fact]
    public async Task Both_AskedOnce_BodyNamesBoth()
    {
        var editor = new FakeUnappliedEditor();
        var state = Unsaved("HIST 101 A");
        var prompts = Asked(ThreeChoiceResult.First, UnsavedChangesGate.Body(state, true));

        Assert.Equal(UnsavedChangesAnswer.Save, await Resolve(state, editor, prompts));

        var call = Assert.Single(prompts.Calls);
        Assert.Contains("HIST 101 A's card has changes", call.Text);
        Assert.Contains("since you last saved at 10:42", call.Text);
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>Save with an editor: it is applied once, and the answer is Save.</summary>
    [Fact]
    public async Task Save_AppliesTheEditor()
    {
        var editor = new FakeUnappliedEditor();
        var prompts = Asked(ThreeChoiceResult.First);

        Assert.Equal(UnsavedChangesAnswer.Save, await Resolve(Unsaved(), editor, prompts));

        Assert.Equal(1, editor.ApplyCalls);
        Assert.Equal(0, editor.DiscardCalls);
    }

    /// <summary>Save, and the editor refuses (validation): stop; nothing discarded; no second message.</summary>
    [Fact]
    public async Task Save_EditorRefuses_Stops()
    {
        var editor = new FakeUnappliedEditor { ApplySucceeds = false };
        var prompts = Asked(ThreeChoiceResult.First);

        Assert.Equal(UnsavedChangesAnswer.Stop, await Resolve(Unsaved(), editor, prompts));

        Assert.Equal(1, editor.ApplyCalls);
        Assert.Equal(0, editor.DiscardCalls);
        Assert.Single(prompts.Calls);
    }

    /// <summary>Save, and the same editor still has changes after an Apply it accepted: stop rather than loop.</summary>
    [Fact]
    public async Task Save_SameEditorStillDirtyAfterAcceptedApply_Stops()
    {
        var editor = new StubbornEditor();
        var prompts = Asked(ThreeChoiceResult.First);

        var answer = await UnsavedChangesGate.ResolveAsync(
            HistoryText.CloseTitle, Unsaved(), () => editor, prompts, () => true);

        Assert.Equal(UnsavedChangesAnswer.Stop, answer);
        Assert.Equal(1, editor.ApplyCalls);
    }

    /// <summary>An editor whose Apply is accepted but which still holds changes afterwards (its list refused to close it).</summary>
    private sealed class StubbornEditor : IUnappliedChanges
    {
        /// <summary>How many times Apply ran.</summary>
        public int ApplyCalls { get; private set; }

        /// <inheritdoc/>
        public bool HasUnappliedChanges => true;

        /// <inheritdoc/>
        public Task<bool> TryApplyAsync() { ApplyCalls++; return Task.FromResult(true); }

        /// <inheritdoc/>
        public void DiscardUnapplied() { }
    }

    /// <summary>Save with only unsaved changes (no editor): the answer is Save.</summary>
    [Fact]
    public async Task Save_NoEditor_ReturnsSave()
        => Assert.Equal(UnsavedChangesAnswer.Save, await Resolve(Unsaved(), null, Asked(ThreeChoiceResult.First)));

    /// <summary>The lock is lost while the question shows, and the user answers Save: stop (the lost-lock path keeps the changes).</summary>
    [Fact]
    public async Task Save_SessionStopsBeingWriterDuringQuestion_Stops()
    {
        var writer = true;
        var prompts = Asked(ThreeChoiceResult.First, before: () => writer = false);

        Assert.Equal(UnsavedChangesAnswer.Stop, await Resolve(Unsaved(), null, prompts, () => writer));
    }

    // ── Don't Save ────────────────────────────────────────────────────────────

    /// <summary>Don't Save: the editor is discarded (not applied) and the answer is DontSave.</summary>
    [Fact]
    public async Task DontSave_DiscardsTheEditor()
    {
        var editor = new FakeUnappliedEditor();
        var prompts = Asked(ThreeChoiceResult.Second);

        Assert.Equal(UnsavedChangesAnswer.DontSave, await Resolve(Unsaved(), editor, prompts));

        Assert.Equal(1, editor.DiscardCalls);
        Assert.Equal(0, editor.ApplyCalls);
    }

    /// <summary>The lock is lost while the question shows, and the user answers Don't Save: stop, and the editor is NOT discarded.</summary>
    [Fact]
    public async Task DontSave_SessionStopsBeingWriterDuringQuestion_StopsAndKeepsTheEditor()
    {
        var writer = true;
        var editor = new FakeUnappliedEditor();
        var prompts = Asked(ThreeChoiceResult.Second, before: () => writer = false);

        Assert.Equal(UnsavedChangesAnswer.Stop, await Resolve(Unsaved(), editor, prompts, () => writer));

        Assert.Equal(0, editor.DiscardCalls);
        Assert.True(editor.Has);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    /// <summary>Cancel (and the window's X, reported as the third button): stop; the editor is untouched.</summary>
    [Fact]
    public async Task Cancel_Stops_AndChangesNothing()
    {
        var editor = new FakeUnappliedEditor();
        var prompts = Asked(ThreeChoiceResult.Third);

        Assert.Equal(UnsavedChangesAnswer.Stop, await Resolve(Unsaved(), editor, prompts));

        Assert.Equal(0, editor.ApplyCalls + editor.DiscardCalls);
        Assert.True(editor.Has);
    }

    // ── Body ──────────────────────────────────────────────────────────────────

    /// <summary>Unsaved only, saved this session: the approved sentence with the last save's time.</summary>
    [Fact]
    public void Body_Unsaved_SavedThisSession()
        => Assert.Equal(
            "You've changed the schedule since you last saved at 10:42. Colleagues won't see these changes until you save.",
            UnsavedChangesGate.Body(Unsaved(), false));

    /// <summary>Unsaved only, nothing saved yet this session: no time is named.</summary>
    [Fact]
    public void Body_Unsaved_NotSavedYet()
        => Assert.Equal(
            "You've changed the schedule and haven't saved yet. Colleagues won't see these changes until you save.",
            UnsavedChangesGate.Body(Unsaved(savedThisSession: false), false));

    /// <summary>Unsaved plus a card with a heading: the card sentence is added.</summary>
    [Fact]
    public void Body_Unsaved_PlusCard()
        => Assert.Equal(
            "You've changed the schedule since you last saved at 10:42. Colleagues won't see these changes until you save. " +
            "HIST 101 A's card has changes you haven't applied; Save applies them too.",
            UnsavedChangesGate.Body(Unsaved("HIST 101 A"), true));

    /// <summary>Unsaved plus an editor without a card heading: the editor sentence is added.</summary>
    [Fact]
    public void Body_Unsaved_PlusEditor_NoHeading()
        => Assert.Equal(
            "You've changed the schedule since you last saved at 10:42. Colleagues won't see these changes until you save. " +
            "The open editor has changes you haven't applied; Save applies them too.",
            UnsavedChangesGate.Body(Unsaved(), true));

    /// <summary>Unsaved, not saved yet, plus a card.</summary>
    [Fact]
    public void Body_Unsaved_NotSavedYet_PlusCard()
        => Assert.Equal(
            "You've changed the schedule and haven't saved yet. Colleagues won't see these changes until you save. " +
            "HIST 101 A's card has changes you haven't applied; Save applies them too.",
            UnsavedChangesGate.Body(Unsaved("HIST 101 A", savedThisSession: false), true));

    /// <summary>A card heading is ignored when no editor has changes (the heading would be stale).</summary>
    [Fact]
    public void Body_Unsaved_HeadingWithoutEditorChanges_IsIgnored()
        => Assert.Equal(
            "You've changed the schedule since you last saved at 10:42. Colleagues won't see these changes until you save.",
            UnsavedChangesGate.Body(Unsaved("HIST 101 A"), false));

    /// <summary>Only a card has changes: the card-only body.</summary>
    [Fact]
    public void Body_CardOnly()
        => Assert.Equal(
            "HIST 101 A's card has changes you haven't applied. Save applies and saves them.",
            UnsavedChangesGate.Body(Clean("HIST 101 A"), true));

    /// <summary>Only another editor has changes: the editor-only body (with a clean state, or a null state).</summary>
    [Fact]
    public void Body_EditorOnly()
    {
        const string expected = "The open editor has changes you haven't applied. Save applies and saves them.";
        Assert.Equal(expected, UnsavedChangesGate.Body(Clean(), true));
        Assert.Equal(expected, UnsavedChangesGate.Body(null, true));
    }

    /// <summary>The titles and buttons are the approved strings.</summary>
    [Fact]
    public void Titles_AndButtons_AreTheApprovedText()
    {
        Assert.Equal("Save Changes?", HistoryText.CloseTitle);
        Assert.Equal("Save your changes to BIOL before opening CHEM?", HistoryText.SwitchTitle("BIOL", "CHEM"));
        Assert.Equal("Save your changes to BIOL before restoring a backup?", HistoryText.RestoreTitle("BIOL"));
        Assert.Equal("Save", HistoryText.SaveButton);
        Assert.Equal("Don't Save", HistoryText.DontSaveButton);
        Assert.Equal("Cancel", HistoryText.CancelButton);
    }
}

/// <summary>Tests of <see cref="UnsavedReminder.DueIn"/>, the two-hour "You haven't saved since" rule.</summary>
public sealed class UnsavedReminderTests
{
    /// <summary>A fixed start of an unsaved stretch.</summary>
    private static readonly DateTime Since = new(2026, 11, 26, 9, 0, 0);

    /// <summary>Nothing unsaved: no notice is due.</summary>
    [Fact]
    public void NothingUnsaved_IsNull()
        => Assert.Null(UnsavedReminder.DueIn(null, null, Since));

    /// <summary>Already reminded for this stretch: no notice is due, however late it is.</summary>
    [Fact]
    public void AlreadyRemindedForThisStretch_IsNull()
        => Assert.Null(UnsavedReminder.DueIn(Since, Since, Since.AddHours(5)));

    /// <summary>At the start of a stretch the full two hours remain.</summary>
    [Fact]
    public void AtTheStart_WaitsTheFullTwoHours()
        => Assert.Equal(TimeSpan.FromHours(2), UnsavedReminder.DueIn(Since, null, Since));

    /// <summary>Part-way through, the rest of the wait remains.</summary>
    [Fact]
    public void PartWay_WaitsTheRest()
        => Assert.Equal(TimeSpan.FromMinutes(30), UnsavedReminder.DueIn(Since, null, Since.AddMinutes(90)));

    /// <summary>When overdue the wait is zero (never negative).</summary>
    [Fact]
    public void Overdue_IsZero()
        => Assert.Equal(TimeSpan.Zero, UnsavedReminder.DueIn(Since, null, Since.AddHours(3)));

    /// <summary>A new stretch after a reminder is due again, two hours from its own start.</summary>
    [Fact]
    public void NewStretchAfterAReminder_IsDueAgain()
    {
        var next = Since.AddHours(4);

        Assert.Equal(TimeSpan.FromHours(2), UnsavedReminder.DueIn(next, Since, next));
        Assert.Equal(TimeSpan.Zero, UnsavedReminder.DueIn(next, Since, next.AddHours(2)));
    }
}
