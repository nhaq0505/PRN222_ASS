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
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [PasswordHash] nvarchar(256) NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [AvatarUrl] nvarchar(500) NULL,
        [BankCode] nvarchar(32) NULL,
        [BankBin] nvarchar(16) NULL,
        [BankAccountNumber] nvarchar(50) NULL,
        [BankAccountHolder] nvarchar(150) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [Trips] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Destination] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [InviteCode] nvarchar(16) NOT NULL,
        [StartDate] date NOT NULL,
        [EndDate] date NOT NULL,
        [EstimatedBudgetPerPerson] decimal(18,0) NOT NULL,
        [Status] int NOT NULL,
        [OwnerUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ClosedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_Trips] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Trips_Users_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [Expenses] (
        [Id] uniqueidentifier NOT NULL,
        [TripId] uniqueidentifier NOT NULL,
        [PaidByUserId] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Category] int NOT NULL,
        [SubtotalAmount] decimal(18,0) NOT NULL,
        [TaxOrTipAmount] decimal(18,0) NOT NULL,
        [DiscountAmount] decimal(18,0) NOT NULL,
        [TotalAmount] decimal(18,0) NOT NULL,
        [SplitMode] int NOT NULL,
        [ReceiptImageUrl] nvarchar(500) NULL,
        [ScanStatus] int NOT NULL,
        [SpentAtUtc] datetime2 NOT NULL,
        [Notes] nvarchar(2000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Expenses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Expenses_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Expenses_Users_PaidByUserId] FOREIGN KEY ([PaidByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TripId] uniqueidentifier NULL,
        [Type] int NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Message] nvarchar(1000) NOT NULL,
        [ActionUrl] nvarchar(500) NULL,
        [IsRead] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notifications_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [Settlements] (
        [Id] uniqueidentifier NOT NULL,
        [TripId] uniqueidentifier NOT NULL,
        [FromUserId] uniqueidentifier NOT NULL,
        [ToUserId] uniqueidentifier NOT NULL,
        [Amount] decimal(18,0) NOT NULL,
        [Status] int NOT NULL,
        [TransferNote] nvarchar(200) NOT NULL,
        [ProofNote] nvarchar(1000) NULL,
        [MarkedPaidAtUtc] datetime2 NULL,
        [ConfirmedAtUtc] datetime2 NULL,
        [DisputeReason] nvarchar(1000) NULL,
        [LastNudgedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Settlements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Settlements_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Settlements_Users_FromUserId] FOREIGN KEY ([FromUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Settlements_Users_ToUserId] FOREIGN KEY ([ToUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [TripDays] (
        [Id] uniqueidentifier NOT NULL,
        [TripId] uniqueidentifier NOT NULL,
        [DayNumber] int NOT NULL,
        [Date] date NOT NULL,
        [Title] nvarchar(200) NULL,
        [Notes] nvarchar(2000) NULL,
        CONSTRAINT [PK_TripDays] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TripDays_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [TripMembers] (
        [Id] uniqueidentifier NOT NULL,
        [TripId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Role] int NOT NULL,
        [IsActive] bit NOT NULL,
        [JoinedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_TripMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TripMembers_Trips_TripId] FOREIGN KEY ([TripId]) REFERENCES [Trips] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TripMembers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [ExpenseSplits] (
        [Id] uniqueidentifier NOT NULL,
        [ExpenseId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [ShareRatioOrWeight] decimal(18,4) NOT NULL,
        [OwedAmount] decimal(18,0) NOT NULL,
        CONSTRAINT [PK_ExpenseSplits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExpenseSplits_Expenses_ExpenseId] FOREIGN KEY ([ExpenseId]) REFERENCES [Expenses] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ExpenseSplits_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [ReceiptItems] (
        [Id] uniqueidentifier NOT NULL,
        [ExpenseId] uniqueidentifier NOT NULL,
        [ItemName] nvarchar(250) NOT NULL,
        [Quantity] int NOT NULL,
        [UnitPrice] decimal(18,0) NOT NULL,
        [LineTotal] decimal(18,0) NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_ReceiptItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReceiptItems_Expenses_ExpenseId] FOREIGN KEY ([ExpenseId]) REFERENCES [Expenses] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [ItineraryItems] (
        [Id] uniqueidentifier NOT NULL,
        [TripDayId] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [LocationName] nvarchar(250) NULL,
        [Address] nvarchar(500) NULL,
        [Latitude] float NULL,
        [Longitude] float NULL,
        [StartTime] time NULL,
        [EndTime] time NULL,
        [EstimatedCost] decimal(18,0) NOT NULL,
        [SortOrder] int NOT NULL,
        [Notes] nvarchar(2000) NULL,
        [IsAiSuggested] bit NOT NULL,
        [ReminderSentAtUtc] datetime2 NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ItineraryItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ItineraryItems_TripDays_TripDayId] FOREIGN KEY ([TripDayId]) REFERENCES [TripDays] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ItineraryItems_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [ReceiptItemClaims] (
        [Id] uniqueidentifier NOT NULL,
        [ReceiptItemId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [ClaimedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ReceiptItemClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReceiptItemClaims_ReceiptItems_ReceiptItemId] FOREIGN KEY ([ReceiptItemId]) REFERENCES [ReceiptItems] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ReceiptItemClaims_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE TABLE [ItineraryVotes] (
        [Id] uniqueidentifier NOT NULL,
        [ItineraryItemId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Vote] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ItineraryVotes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ItineraryVotes_ItineraryItems_ItineraryItemId] FOREIGN KEY ([ItineraryItemId]) REFERENCES [ItineraryItems] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ItineraryVotes_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Expenses_PaidByUserId] ON [Expenses] ([PaidByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Expenses_TripId] ON [Expenses] ([TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExpenseSplits_ExpenseId_UserId] ON [ExpenseSplits] ([ExpenseId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ExpenseSplits_UserId] ON [ExpenseSplits] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ItineraryItems_CreatedByUserId] ON [ItineraryItems] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ItineraryItems_TripDayId] ON [ItineraryItems] ([TripDayId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ItineraryVotes_ItineraryItemId_UserId] ON [ItineraryVotes] ([ItineraryItemId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ItineraryVotes_UserId] ON [ItineraryVotes] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_TripId] ON [Notifications] ([TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_IsRead_CreatedAtUtc] ON [Notifications] ([UserId], [IsRead], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReceiptItemClaims_ReceiptItemId_UserId] ON [ReceiptItemClaims] ([ReceiptItemId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ReceiptItemClaims_UserId] ON [ReceiptItemClaims] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ReceiptItems_ExpenseId] ON [ReceiptItems] ([ExpenseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Settlements_FromUserId] ON [Settlements] ([FromUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Settlements_ToUserId] ON [Settlements] ([ToUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Settlements_TripId] ON [Settlements] ([TripId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TripDays_TripId_DayNumber] ON [TripDays] ([TripId], [DayNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TripMembers_TripId_UserId] ON [TripMembers] ([TripId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TripMembers_UserId] ON [TripMembers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Trips_InviteCode] ON [Trips] ([InviteCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Trips_OwnerUserId] ON [Trips] ([OwnerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261115201335_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261115201335_InitialCreate', N'10.0.15');
END;

COMMIT;
GO

