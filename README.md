# TermPoint

A desktop application for visualizing departmental course schedules. TermPoint helps universisty and college department chairs, scheduling assistants, and advisors see schedule conflicts, room utilization, and instructor load clearly enough to make good decisions — before that schedule gets entered into their institution's official system of record. See www.termpoint.ca for a web-based demonstration.

## What TermPoint is (and isn't)

TermPoint is a **visualization tool**, not an optimizer and not a system of record. It doesn't generate schedules, solve constraint problems, or replace Banner, Infosilem, Coursedog, Ad Astra, or CourseLeaf CLSS. It sits **upstream** of those systems: you design and refine a term's schedule in TermPoint, then enter the finalized version into your institutional SIS. It fills a gap in that institutional SIS typically don't offer good visualization and editing tools; the assumption is that they'll do the scheduling for you. Many institutions fall in a gap however in which the SIS is only used as the system of **record** for course schedules, rather than
as a means of schedule **creation**. Timetable preparation is left to the department-level personnel to manage, often via spreadsheets. Spreadsheets can work for small departments with a few courses but have serious
drawbacks in departments of any size. If all the sections are on one sheet, the view is much too cluttered and complex to effectively manage. If different aspects of the schedule are distributed over multiple sheets, changes in one sheet often necessitate changes in another, with great potential for oversight and error.

TermPoint is offered as a solution, providing the user charged with making scheduling decisions an effective tool for making them. The premise is simple: most scheduling conflicts and inefficiencies aren't hard to *catch* — they're hard to *see*, when the alternative is a spreadsheet or a stack of paper. Good visual information lets a human make a good call. TermPoint's job is to make that information legible, not to make the call for you.

## Who it's for

- Department chairs building a term's schedule
- Scheduling assistants/coordinators managing room and instructor assignments
- Academic advisors who need to see schedule structure to advise students accurately

## Status

TermPoint is in active development.

## Distribution
TermPoint is a Windows desktop application which will be distributed for university and college use via MicroSoft Store. There is a web-based demonstration version available at www.termpoint.ca, and those interested can sign up for an early-access program there.

## Code Inspection and Security Evaluation  

The source code is open for inspection here on GitHub. Subject to the conditions of the license below, evaluators may download, view, compile and analyze the source code solely for purposes of evaluating, auditing, testing, or reviewing the security, reliability, and operation of TermPoint.


## Data and privacy

TermPoint stores schedule data locally in a SQLite datbase. There is no server component and no telemetry collection beyond the exception reporting and update tracking offered in MicroSoft store product. TermPoint does not transmit your schedule data anywhere.

## License

END-USER LICENSE AGREEMENT FOR TermPoint


This End-User License Agreement ("EULA") is a legal agreement between you (either an individual or a single entity) and Greg Schlitt (hereinafter referred to as "Licensor"), for the software product(s) identified above which may include associated software components, media, printed materials, and "online" or electronic documentation ("SOFTWARE PRODUCT"). By installing, copying, or otherwise using the SOFTWARE PRODUCT, you agree to be bound by the terms of this EULA. This license agreement represents the entire agreement concerning the program between You and the Licensor, and it supersedes any prior proposal, representation, or understanding between the parties. If you do not agree to the terms of this EULA, do not install or use the SOFTWARE PRODUCT.

The SOFTWARE PRODUCT is protected by copyright laws and international copyright treaties, as well as other intellectual property laws and treaties. The SOFTWARE PRODUCT is licensed, not sold.

1. GRANT OF LICENSE.
The SOFTWARE PRODUCT is licensed as follows:
(a) Installation and Use.
The Licensor grants you the right to install and use copies of the SOFTWARE PRODUCT on your computer running a validly licensed copy of the operating system for which the SOFTWARE PRODUCT was designed.
(b) Backup Copies.
You may also make copies of the SOFTWARE PRODUCT as may be necessary for backup and archival purposes.

2. DESCRIPTION OF OTHER RIGHTS AND LIMITATIONS.
(a) Maintenance of Copyright Notices.
You must not remove or alter any copyright notices on any and all copies of the SOFTWARE PRODUCT.
(b) Distribution.
You may not distribute copies of the SOFTWARE PRODUCT to third parties. Evaluation versions available for download from the Licensor's websites may be freely distributed.
(c) Prohibition on Reverse Engineering, Decompilation, and Disassembly.
You may not reverse engineer, decompile, or disassemble the SOFTWARE PRODUCT, except and only to the extent that such activity is expressly permitted by applicable law notwithstanding this limitation.
(d) Rental.
You may not rent, lease, or lend the SOFTWARE PRODUCT.
(e) Support Services.
The Licensor may provide you with support services related to the SOFTWARE PRODUCT ("Support Services"). Any supplemental software code provided to you as part of the Support Services shall be considered part of the SOFTWARE PRODUCT and subject to the terms and conditions of this EULA.
(f) Compliance with Applicable Laws.
You must comply with all applicable laws regarding use of the SOFTWARE PRODUCT.
(g) Source Code. The Licensor may make source code available for inspection. You may download, view, and analyze the source code solely for purposes of evaluating, auditing, testing, or reviewing the security, reliability, and operation of the SOFTWARE PRODUCT. You may compile and modify the source code for your own internal evaluation purposes or to propose contributions to the SOFTWARE PRODUCT. Except as expressly permitted by this Agreement, no rights are granted to distribute modified versions or derivative works of the SOFTWARE PRODUCT.

3. TERMINATION
Without prejudice to any other rights, the Licensor may terminate this EULA if you fail to comply with the terms and conditions of this EULA. In such event, you must destroy all copies of the SOFTWARE PRODUCT in your possession.

## Support / Contact
admin@termpoint.ca

