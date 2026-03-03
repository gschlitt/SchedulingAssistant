# Test Scenario: Recent Audit Fixes

## Overview

This document outlines manual testing scenarios for the critical fixes implemented from the code audit:

1. **Memory leak fix** — Transient ViewModels properly disposing when flyouts close
2. **Error handling fix** — Database operations now show user-visible errors instead of silently failing or crashing

---

## Fix 1: Memory Leak (InstructorListViewModel + LegalStartTimeListViewModel)

### What was fixed

- `InstructorListViewModel` and `LegalStartTimeListViewModel` are transient (created new each time the flyout opens)
- Both subscribed to `SemesterContext.PropertyChanged` (a singleton event) without unsubscribing
- This caused the singleton to hold references to old VM instances, preventing garbage collection
- **Solution**: Both VMs now implement `IDisposable` and unsubscribe in `Dispose()`. `MainWindowViewModel` auto-disposes old flyout pages when a new one opens.

### Test Scenario 1a: Instructors Flyout Cleanup

**Precondition**: App is running with a semester selected.

**Steps**:
1. Open **Instructors** flyout (via Menu > Instructors)
2. Verify the Instructors list appears ✓
3. Close the flyout (press Escape or click close button)
4. Repeat steps 1–3 five more times (open/close Instructors 6 times total)

**Expected behavior**:
- Each time you close, the old `InstructorListViewModel` instance should be garbage collected
- No memory accumulation; flyout opens quickly each time
- (Advanced: If you set a breakpoint in `InstructorListViewModel.Dispose()`, it should fire each time you close the flyout)

**Pass/Fail**: ☐ Pass if flyout opens consistently without slowdown after repeated open/close cycles

---

### Test Scenario 1b: Legal Start Times Flyout Cleanup

**Precondition**: App is running with a semester selected.

**Steps**:
1. Open **Section Properties > Scheduling** (legal start times)
2. Verify the start times list for the current semester appears ✓
3. Close the flyout
4. Switch to a different semester
5. Open **Scheduling** again
6. Repeat steps 1–5 three times

**Expected behavior**:
- Each open/close and semester switch should clean up the old `LegalStartTimeListViewModel`
- No memory leaks; responsive UI
- When you switch semesters, the legal start times reload for the new semester (the VM reloads on `SemesterContext.PropertyChanged` change)

**Pass/Fail**: ☐ Pass if UI remains responsive and times reload correctly per semester

---

## Fix 2: Error Handling (All Save and Delete Operations)

### What was fixed

- All save operations that can throw database exceptions now wrap in try-catch and display user-visible error dialogs
- Previously, some operations would crash the app or silently fail with no feedback
- All `onSave` callbacks in edit ViewModels are now `async Task` instead of `void`, allowing proper error dialog display via `await ShowError(...)`
- All save operations (`Add`/`Edit` buttons) now show error dialogs on database failure
- All delete operations (`Delete` buttons) already had error handling and now display properly

### Affected Edit ViewModels (Now Async)

- `SectionEditViewModel` — Section Add/Edit
- `CourseEditViewModel` — Course Add/Edit
- `SubjectEditViewModel` — Subject Add/Edit
- `LegalStartTimeEditViewModel` — Legal Start Time Add/Edit

### Test Scenario 2a: Delete Error Handling — Sections

**Setup**:
- Have at least one section in the current semester

**Steps**:
1. In the **Sections** panel, select a section
2. Click the **Delete** button in the header
3. Simulate a database failure:
   - Make the database file read-only (Right-click DB file > Properties > Read-only ☑)
   - OR temporarily move the database file to an inaccessible location
4. The delete should fail
5. Verify an **"Error"** dialog appears with message: `"The delete could not be completed. Please try again."`
6. Click **OK** to close the error dialog
7. Undo the database access restriction (make file writable / restore file)
8. Delete the section again — it should now succeed

**Expected behavior**:
- First attempt shows error dialog (not a crash, not silent failure)
- Second attempt succeeds and section is removed from list
- Error is logged in application logs

**Pass/Fail**: ☐ Pass if error dialog appears and operation can be retried successfully

---

### Test Scenario 2b: Delete Error Handling — Instructors

**Setup**:
- Have at least one instructor with no assigned sections (so delete is allowed)

**Steps**:
1. Open **Instructors** flyout
2. Select an instructor
3. Click **Delete** button
4. A confirmation dialog appears — click **Delete** to confirm
5. Make the database read-only (same as 2a step 3)
6. The delete should fail
7. Verify an **"Error"** dialog appears
8. Click **OK**
9. Restore database write access
10. Try deleting again — it should succeed

**Expected behavior**:
- Error dialog surfaces the failure (not silent)
- Instructor remains in list after failed delete
- Successful delete on retry removes instructor

**Pass/Fail**: ☐ Pass if error handling works consistently

---

### Test Scenario 2c: Save Error Handling — Add Section

**Setup**:
- At least one course, instructor, and room exist in the system

**Steps**:
1. In the **Sections** panel, click **Add** button
2. A new section editor appears
3. Enter:
   - Subject/Course: (select from dropdown)
   - Section Code: `SEC01` (or any unique code)
4. Fill in other required fields (Instructor, Room, etc. if needed)
5. Make the database read-only
6. Click **Save** (or finalize the form save)
7. Verify an **"Error"** dialog appears: `"The save could not be completed. Please try again."`
8. Click **OK**
9. Restore database write access
10. Click **Save** again — it should now succeed
11. Verify the new section appears in the sections list

**Expected behavior**:
- First save attempt shows error dialog (not a crash, not silent failure)
- Section editor remains open with data intact
- Second save succeeds and section is added to list

**Pass/Fail**: ✓ Pass if error dialog appears and retry succeeds

---

### Test Scenario 2c-alt: Save Error Handling — Add Course

**Setup**:
- None required

**Steps**:
1. Open **Courses** flyout
2. Click **Add** button
3. A new course editor appears
4. Enter:
   - Subject: (select one from dropdown)
   - Course Code: `TST101` (or any unique code)
   - Title: `Test Course`
5. Make the database read-only
6. Click **Save** (or however the form saves — likely a button in the expanded editor)
7. Verify an **"Error"** dialog appears: `"The save could not be completed. Please try again."`
8. Click **OK**
9. Restore database write access
10. Click **Save** again — it should now succeed
11. Verify the new course appears in the courses list

**Expected behavior**:
- First save attempt shows error dialog
- Form remains open with data intact (user can retry without re-entering everything)
- Second save succeeds and course is added to list

**Pass/Fail**: ☐ Pass if error dialog appears and retry succeeds

---

### Test Scenario 2d: Delete Error Handling — Academic Year (Cascading)

**Setup**:
- At least one academic year with no sections (or confirm deletion allows it)

**Steps**:
1. Open **Academic Years** flyout
2. Select an academic year
3. Click **Delete**
4. Confirmation dialog appears (showing section count) — click **Delete**
5. Make the database read-only (same as 2a step 3)
6. The cascading delete should fail (it deletes semesters within the year first, then the year)
7. Verify error dialog: `"The delete could not be completed. Please try again."`
8. Click **OK**
9. Restore database write access
10. Click **Delete** again — it should succeed
11. Verify academic year is removed from the list

**Expected behavior**:
- Transactional integrity: even if the delete fails partway through, the error is caught
- User is informed of failure
- Retry succeeds

**Pass/Fail**: ☐ Pass if cascading delete error is handled gracefully

---

### Test Scenario 2d-alt: Save Error Handling — Add Academic Year

**Setup**:
- None required

**Steps**:
1. Open **Academic Years** flyout
2. Click **Add** button
3. An editor appears for a new academic year
4. Enter a name (e.g., `2026-2027`)
5. Make the database read-only
6. Click **Save** (or finalize the form save)
7. Verify an **"Error"** dialog appears: `"The save could not be completed. Please try again."`
8. Click **OK**
9. Restore database write access
10. Click **Save** again — it should now succeed
11. Verify the new academic year appears in the academic years list

**Expected behavior**:
- First save attempt shows error dialog (not a crash, not silent failure)
- Form remains open with data intact
- Second save succeeds and academic year is added to list
- If this is the first academic year, you may be prompted to import persisted start times or confirm other setup

**Pass/Fail**: ☐ Pass if error dialog appears and retry succeeds

---

### Test Scenario 2e: Save Error Handling — Add Legal Start Time

**Setup**:
- A semester is selected in the dropdown

**Steps**:
1. Open **Section Properties > Scheduling** (Legal Start Times)
2. Click **Add**
3. Editor appears for a new legal start time entry
4. Enter values (e.g., Block Length: 1.5, Start Times: 08:00, 09:30)
5. Make the database read-only
6. Click **Save**
7. Verify error dialog: `"The save could not be completed. Please try again."`
8. Click **OK**
9. Restore database write access
10. Click **Save** again
11. Verify the entry is added to the list

**Expected behavior**:
- Error dialog on first save
- Form retains data for retry
- Second save succeeds
- Entry appears in list

**Pass/Fail**: ☐ Pass if error handling works

---

## Regression Testing Checklist

After passing the above scenarios, verify no regressions:

- ☐ **Section operations**: Add, Edit, Delete a section — all work normally
- ☐ **Instructor operations**: Add, Edit, Delete an instructor — all work normally
- ☐ **Course operations**: Add, Edit, Delete a course — all work normally
- ☐ **Subject operations**: Add, Edit, Delete a subject — all work normally
- ☐ **Cross-view sync**: Click a section in the grid → appears selected in Section List and Workload View
- ☐ **Flyout navigation**: All management flyouts open/close without errors
- ☐ **Schedule Grid renders**: Grid displays sections correctly with no visual glitches
- ☐ **Database persistence**: Close and reopen the app — data is intact

---

## Notes for Test Execution

### Making Database Read-Only (Windows)
```
1. Right-click database file
2. Properties
3. Check "Read-only"
4. OK
5. (To restore: uncheck "Read-only")
```

### Alternative: Move Database Temporarily
```
1. Close the app
2. Move the database file to a different folder
3. Reopen the app (it will try to load the missing DB and error gracefully)
4. Move file back and reopen
```

### Checking Logs
- Logs are written to: `%AppData%\SchedulingAssistant\logs\`
- Each error operation should create a log entry with the exception details
- Verify error messages are being logged

### Key Indicators of Success
- **No crashes** on error conditions
- **User-visible error dialogs** appear (not silent failures)
- **Retry is possible** without losing form data
- **No memory leaks** (app remains responsive after repeated flyout open/close)
- **All operations proceed normally** when database is accessible

---

## Summary

| Test | Category | Scenario | Pass/Fail |
|------|----------|----------|-----------|
| 1a | Memory Leak | Instructors flyout repeated open/close | ☐ |
| 1b | Memory Leak | Legal Start Times with semester switch | ☐ |
| 2a | Error Handling | Delete section with DB read-only | ☐ |
| 2b | Error Handling | Delete instructor with DB read-only | ☐ |
| 2c | Error Handling | **Add section with DB read-only** | ✓ |
| 2c-alt | Error Handling | Add course with DB read-only | ☐ |
| 2d | Error Handling | Delete academic year with DB read-only | ☐ |
| 2d-alt | Error Handling | Add academic year with DB read-only | ☐ |
| 2e | Error Handling | Add legal start time with DB read-only | ☐ |
| Regression | General | All normal operations work | ☐ |

Once all tests pass, the audit fixes are verified and ready for deployment.
