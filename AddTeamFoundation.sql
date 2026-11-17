START TRANSACTION;

ALTER TABLE `MDRecords` ADD `TeamId` bigint NULL;

CREATE TABLE `TeamAuditEvents` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `TimestampUtc` datetime(6) NOT NULL,
    `Username` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Action` varchar(80) CHARACTER SET utf8mb4 NOT NULL,
    `DetailsJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_TeamAuditEvents` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `Teams` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Name` varchar(120) CHARACTER SET utf8mb4 NOT NULL,
    `NormalizedName` varchar(120) CHARACTER SET utf8mb4 NOT NULL,
    `LeadUserId` bigint NULL,
    `Version` char(36) COLLATE ascii_general_ci NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `CreatedBy` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_Teams` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_Teams_PortalUsers_LeadUserId` FOREIGN KEY (`LeadUserId`) REFERENCES `PortalUsers` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `TeamManagers` (
    `TeamId` bigint NOT NULL,
    `UserId` bigint NOT NULL,
    CONSTRAINT `PK_TeamManagers` PRIMARY KEY (`TeamId`, `UserId`),
    CONSTRAINT `FK_TeamManagers_PortalUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `PortalUsers` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TeamManagers_Teams_TeamId` FOREIGN KEY (`TeamId`) REFERENCES `Teams` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE TABLE `TeamMemberships` (
    `UserId` bigint NOT NULL,
    `TeamId` bigint NOT NULL,
    `Version` char(36) COLLATE ascii_general_ci NOT NULL,
    CONSTRAINT `PK_TeamMemberships` PRIMARY KEY (`UserId`),
    CONSTRAINT `FK_TeamMemberships_PortalUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `PortalUsers` (`Id`) ON DELETE RESTRICT,
    CONSTRAINT `FK_TeamMemberships_Teams_TeamId` FOREIGN KEY (`TeamId`) REFERENCES `Teams` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_MDRecords_TeamId` ON `MDRecords` (`TeamId`);

CREATE INDEX `IX_TeamManagers_UserId` ON `TeamManagers` (`UserId`);

CREATE INDEX `IX_TeamMemberships_TeamId` ON `TeamMemberships` (`TeamId`);

CREATE UNIQUE INDEX `IX_Teams_LeadUserId` ON `Teams` (`LeadUserId`);

CREATE UNIQUE INDEX `IX_Teams_NormalizedName` ON `Teams` (`NormalizedName`);

ALTER TABLE `MDRecords` ADD CONSTRAINT `FK_MDRecords_Teams_TeamId` FOREIGN KEY (`TeamId`) REFERENCES `Teams` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261029120000_AddTeamFoundation', '8.0.13');

COMMIT;
