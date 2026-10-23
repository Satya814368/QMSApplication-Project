START TRANSACTION;

CREATE TABLE `DeliverableTasks` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `DeliverableId` bigint NOT NULL,
    `Language` varchar(80) CHARACTER SET utf8mb4 NOT NULL,
    `AssignedUserId` bigint NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CreatedBy` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_DeliverableTasks` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_DeliverableTasks_MDRecords_DeliverableId` FOREIGN KEY (`DeliverableId`) REFERENCES `MDRecords` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_DeliverableTasks_PortalUsers_AssignedUserId` FOREIGN KEY (`AssignedUserId`) REFERENCES `PortalUsers` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `DeliverableTargets` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `TaskId` bigint NOT NULL,
    `TargetNumber` varchar(40) CHARACTER SET utf8mb4 NOT NULL,
    `AssignedUserId` bigint NOT NULL,
    `IsCancelled` tinyint(1) NOT NULL,
    `CancellationReason` varchar(500) CHARACTER SET utf8mb4 NULL,
    `GnApprovalReference` varchar(200) CHARACTER SET utf8mb4 NULL,
    `CancelledAtUtc` datetime(6) NULL,
    `UpdatedAtUtc` datetime(6) NOT NULL,
    `UpdatedBy` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `ConcurrencyToken` char(36) COLLATE ascii_general_ci NOT NULL,
    CONSTRAINT `PK_DeliverableTargets` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_DeliverableTargets_DeliverableTasks_TaskId` FOREIGN KEY (`TaskId`) REFERENCES `DeliverableTasks` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_DeliverableTargets_PortalUsers_AssignedUserId` FOREIGN KEY (`AssignedUserId`) REFERENCES `PortalUsers` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `TargetProgressSteps` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `TargetId` bigint NOT NULL,
    `StepNumber` int NOT NULL,
    `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `MinutesSpent` bigint NOT NULL,
    `CompletedAtUtc` datetime(6) NULL,
    CONSTRAINT `PK_TargetProgressSteps` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_TargetProgressSteps_DeliverableTargets_TargetId` FOREIGN KEY (`TargetId`) REFERENCES `DeliverableTargets` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_DeliverableTasks_AssignedUserId` ON `DeliverableTasks` (`AssignedUserId`);

CREATE UNIQUE INDEX `IX_DeliverableTasks_DeliverableId_Language` ON `DeliverableTasks` (`DeliverableId`, `Language`);

CREATE INDEX `IX_DeliverableTargets_AssignedUserId` ON `DeliverableTargets` (`AssignedUserId`);

CREATE UNIQUE INDEX `IX_DeliverableTargets_TaskId_TargetNumber` ON `DeliverableTargets` (`TaskId`, `TargetNumber`);

CREATE UNIQUE INDEX `IX_TargetProgressSteps_TargetId_StepNumber` ON `TargetProgressSteps` (`TargetId`, `StepNumber`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261022120000_AddLanguageTasks', '8.0.13');

COMMIT;

