IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(100) NOT NULL,
        [Email] nvarchar(150) NOT NULL,
        [PhoneNumber] nvarchar(20) NOT NULL,
        [PasswordHash] nvarchar(256) NOT NULL,
        [Role] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE TABLE [Venues] (
        [Id] int NOT NULL IDENTITY,
        [Slug] nvarchar(120) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [District] nvarchar(100) NOT NULL,
        [Address] nvarchar(250) NOT NULL,
        [PhoneNumber] nvarchar(25) NOT NULL,
        [Description] nvarchar(600) NOT NULL,
        [AccentColor] nvarchar(30) NOT NULL,
        [CoverImageUrl] nvarchar(500) NOT NULL,
        [GalleryImageUrls] nvarchar(2000) NOT NULL,
        [OpenHour] int NOT NULL,
        [CloseHour] int NOT NULL,
        [OwnerId] int NOT NULL,
        [Status] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Venues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Venues_Users_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE TABLE [Courts] (
        [Id] int NOT NULL IDENTITY,
        [VenueId] int NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [SportType] int NOT NULL,
        [IsIndoor] bit NOT NULL,
        [Surface] nvarchar(100) NOT NULL,
        [ImageUrl] nvarchar(500) NULL,
        [BasePricePerHour] decimal(18,0) NOT NULL,
        [PeakPricePerHour] decimal(18,0) NOT NULL,
        [IsActive] bit NOT NULL,
        [DisplayOrder] int NOT NULL,
        CONSTRAINT [PK_Courts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Courts_Venues_VenueId] FOREIGN KEY ([VenueId]) REFERENCES [Venues] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE TABLE [VenueReviews] (
        [Id] int NOT NULL IDENTITY,
        [VenueId] int NOT NULL,
        [UserId] int NOT NULL,
        [Rating] int NOT NULL,
        [Comment] nvarchar(600) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_VenueReviews] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_VenueReviews_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_VenueReviews_Venues_VenueId] FOREIGN KEY ([VenueId]) REFERENCES [Venues] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE TABLE [Bookings] (
        [Id] int NOT NULL IDENTITY,
        [BookingCode] nvarchar(30) NOT NULL,
        [CourtId] int NOT NULL,
        [UserId] int NOT NULL,
        [PlayDate] date NOT NULL,
        [StartHour] int NOT NULL,
        [PriceVnd] decimal(18,0) NOT NULL,
        [Status] int NOT NULL,
        [HoldExpiresAtUtc] datetime2 NULL,
        [Notes] nvarchar(500) NULL,
        [PaymentMethod] nvarchar(50) NOT NULL,
        [IsPaid] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ConfirmedAtUtc] datetime2 NULL,
        [CheckedInAtUtc] datetime2 NULL,
        [CancelledAtUtc] datetime2 NULL,
        CONSTRAINT [PK_Bookings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Bookings_Courts_CourtId] FOREIGN KEY ([CourtId]) REFERENCES [Courts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Bookings_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Bookings_BookingCode] ON [Bookings] ([BookingCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Bookings_CourtId_PlayDate_StartHour] ON [Bookings] ([CourtId], [PlayDate], [StartHour]) WHERE [Status] IN (1, 2, 3)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE INDEX [IX_Bookings_PlayDate] ON [Bookings] ([PlayDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE INDEX [IX_Bookings_UserId_Status] ON [Bookings] ([UserId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Courts_VenueId_Code] ON [Courts] ([VenueId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE INDEX [IX_Courts_VenueId_DisplayOrder] ON [Courts] ([VenueId], [DisplayOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE INDEX [IX_VenueReviews_UserId] ON [VenueReviews] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE INDEX [IX_VenueReviews_VenueId] ON [VenueReviews] ([VenueId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE INDEX [IX_Venues_OwnerId] ON [Venues] ([OwnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Venues_Slug] ON [Venues] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    CREATE INDEX [IX_Venues_Status_District] ON [Venues] ([Status], [District]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261116064234_InitialCourtBookV6'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261116064234_InitialCourtBookV6', N'10.0.15');
END;

COMMIT;
GO

