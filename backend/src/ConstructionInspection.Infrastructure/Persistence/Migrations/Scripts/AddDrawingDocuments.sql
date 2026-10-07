BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007094511_AddDrawingDocuments'
)
BEGIN
    CREATE TABLE [DrawingDocuments] (
        [Id] uniqueidentifier NOT NULL,
        [OriginalFileName] nvarchar(255) NOT NULL,
        [StorageKey] varchar(500) NOT NULL,
        [ContentType] varchar(100) NOT NULL,
        [FileSize] bigint NOT NULL,
        [Sha256] char(64) NOT NULL,
        [PageCount] int NOT NULL,
        [Status] varchar(20) NOT NULL,
        [CreatedAt] datetime2(7) NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_DrawingDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DrawingDocuments_Status] CHECK ([Status] IN ('Uploading','Ready','Failed')),
        CONSTRAINT [FK_DrawingDocuments_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007094511_AddDrawingDocuments'
)
BEGIN
    CREATE INDEX [IX_DrawingDocuments_CreatedByUserId] ON [DrawingDocuments] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007094511_AddDrawingDocuments'
)
BEGIN
    CREATE INDEX [IX_DrawingDocuments_Sha256] ON [DrawingDocuments] ([Sha256]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007094511_AddDrawingDocuments'
)
BEGIN
    CREATE INDEX [IX_DrawingDocuments_Status_CreatedAt] ON [DrawingDocuments] ([Status], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007094511_AddDrawingDocuments'
)
BEGIN
    CREATE UNIQUE INDEX [UX_DrawingDocuments_StorageKey] ON [DrawingDocuments] ([StorageKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007094511_AddDrawingDocuments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007094511_AddDrawingDocuments', N'10.0.12');
END;

COMMIT;
GO
