-- Run ONCE as database administrator. Stop if any statement fails.
USE qmsportaldb;

ALTER TABLE `Guidelines` ADD `ContentIsPlainText` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `Guidelines` ADD `TargetRoles` int NOT NULL DEFAULT 31;

ALTER TABLE `Guidelines` ADD `UpdatedBy` varchar(100) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `Guidelines` ADD `Version` char(36) COLLATE ascii_general_ci NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

CREATE TABLE `GuidelineReadStatuses` (
    `UserId` bigint NOT NULL,
    `GuidelineId` bigint NOT NULL,
    `SeenVersion` char(36) COLLATE ascii_general_ci NOT NULL,
    `ReadAtUtc` datetime(6) NOT NULL,
    CONSTRAINT `PK_GuidelineReadStatuses` PRIMARY KEY (`UserId`, `GuidelineId`),
    CONSTRAINT `FK_GuidelineReadStatuses_Guidelines_GuidelineId` FOREIGN KEY (`GuidelineId`) REFERENCES `Guidelines` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_GuidelineReadStatuses_PortalUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `PortalUsers` (`Id`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_GuidelineReadStatuses_GuidelineId` ON `GuidelineReadStatuses` (`GuidelineId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`,`ProductVersion`) VALUES ('20261117120000_AddGuidelineUpdates','8.0.13');
