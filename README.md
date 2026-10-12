# Book Club App

## Description
Book Club App is a mobile application for readers to discuss digital
books together. The planned features include uploading a book, commenting
on individual pages, and replying to other readers' comments without
changing the book's text.

The app will support reading downloaded books and writing comments
offline, with changes synchronized when an internet connection is
available.

## Domain Entity: PageComment
PageComment represents a comment attached to a specific book and page.

Properties:
- Id: Unique identifier for the comment.
- BookId: Identifier of the book being discussed.
- PageNumber: Page the comment refers to.
- AuthorId: Identifier of the comment's author.
- AuthorName: Display name of the author.
- CommentText: Content of the comment.
- CreatedAt: UTC date and time the comment was created.
- UpdatedAt: UTC date and time the comment was last updated.
- ParentCommentId: Optional reference to another comment for replies.

## Week 1 – Planning & Setup
- [x] Choose domain entity: PageComment.
- [x] Set up MAUI project.
- [ ] Set up SQLite.
- [ ] Set up server or mock API.
- [ ] Complete README with app name, description, and Week 1 progress.
- [ ] Submit screenshot of rendered README and zipped .git file to Canvas.

## Current App Status
The app currently displays the default MAUI starter screen.
The PageComment model has been added. Book uploading, page discussions,
offline storage, and synchronization are planned features and are not
implemented yet.

## Planned Synchronization Behavior
Synchronization must preserve separate comments added to the same page
online or offline. Conflicting edits to the same comment must be detected
and handled without silently discarding either version. Offline comments
must remain stored locally until the server confirms they were saved.

## Development Environment
- .NET SDK: 11.0.100-preview.7.26381.103
- Android target: net11.0-android
- Editor: Visual Studio Code
- Test device: MAUI android 35 emulator