BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914125320_AddConstructionObjects'
)
BEGIN
    CREATE TABLE [ConstructionObjects] (
        [Id] uniqueidentifier NOT NULL,
        [ParentId] uniqueidentifier NULL,
        [Code] nvarchar(100) NOT NULL,
        [Name] nvarchar(250) NOT NULL,
        [ObjectType] varchar(20) NOT NULL,
        [Location] nvarchar(500) NULL,
        [Description] nvarchar(2000) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2(7) NOT NULL,
        [UpdatedByUserId] uniqueidentifier NULL,
        [UpdatedAt] datetime2(7) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_ConstructionObjects] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ConstructionObjects_ObjectType] CHECK ([ObjectType] IN ('Project','Building','Floor','Room','WorkItem')),
        CONSTRAINT [FK_ConstructionObjects_AspNetUsers_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_ConstructionObjects_AspNetUsers_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [AspNetUsers] ([Id]),
        CONSTRAINT [FK_ConstructionObjects_ConstructionObjects_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [ConstructionObjects] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914125320_AddConstructionObjects'
)
BEGIN
    CREATE INDEX [IX_ConstructionObjects_CreatedByUserId] ON [ConstructionObjects] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914125320_AddConstructionObjects'
)
BEGIN
    CREATE INDEX [IX_ConstructionObjects_IsActive_ObjectType] ON [ConstructionObjects] ([IsActive], [ObjectType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914125320_AddConstructionObjects'
)
BEGIN
    CREATE INDEX [IX_ConstructionObjects_ParentId] ON [ConstructionObjects] ([ParentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914125320_AddConstructionObjects'
)
BEGIN
    CREATE INDEX [IX_ConstructionObjects_UpdatedByUserId] ON [ConstructionObjects] ([UpdatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914125320_AddConstructionObjects'
)
BEGIN
    CREATE UNIQUE INDEX [UX_ConstructionObjects_Code] ON [ConstructionObjects] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914125320_AddConstructionObjects'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260914125320_AddConstructionObjects', N'10.0.12');
END;

COMMIT;
GO
