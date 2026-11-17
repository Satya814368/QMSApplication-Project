-- Apply once after AddTeamFoundation. Back up qmsportaldb first. Use a database administrator.
USE `qmsportaldb`;
START TRANSACTION;

ALTER TABLE `Teams` ADD `DepartmentId` bigint NULL;

ALTER TABLE `PortalUsers` ADD `DepartmentId` bigint NULL;

ALTER TABLE `PortalUsers` ADD `IsApproved` tinyint(1) NOT NULL DEFAULT TRUE;

ALTER TABLE `PortalUsers` ADD `ManagerDepartmentScope` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `PortalUsers` ADD `SecurityStamp` char(36) COLLATE ascii_general_ci NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

ALTER TABLE `PortalUsers` ADD `Version` char(36) COLLATE ascii_general_ci NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

CREATE TABLE `Departments` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Name` varchar(120) CHARACTER SET utf8mb4 NOT NULL,
    `NormalizedName` varchar(120) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK_Departments` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `PasswordResetRequests` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UserId` bigint NOT NULL,
    `RequestedAtUtc` datetime(6) NOT NULL,
    `TokenHash` varchar(64) CHARACTER SET utf8mb4 NULL,
    `ExpiresAtUtc` datetime(6) NULL,
    `IsClosed` tinyint(1) NOT NULL,
    `Version` char(36) COLLATE ascii_general_ci NOT NULL,
    CONSTRAINT `PK_PasswordResetRequests` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PasswordResetRequests_PortalUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `PortalUsers` (`Id`) ON DELETE RESTRICT
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_Teams_DepartmentId` ON `Teams` (`DepartmentId`);

CREATE INDEX `IX_PortalUsers_DepartmentId` ON `PortalUsers` (`DepartmentId`);

CREATE UNIQUE INDEX `IX_Departments_NormalizedName` ON `Departments` (`NormalizedName`);

CREATE UNIQUE INDEX `IX_PasswordResetRequests_TokenHash` ON `PasswordResetRequests` (`TokenHash`);

CREATE INDEX `IX_PasswordResetRequests_UserId_RequestedAtUtc` ON `PasswordResetRequests` (`UserId`, `RequestedAtUtc`);

ALTER TABLE `PortalUsers` ADD CONSTRAINT `FK_PortalUsers_Departments_DepartmentId` FOREIGN KEY (`DepartmentId`) REFERENCES `Departments` (`Id`) ON DELETE RESTRICT;

ALTER TABLE `Teams` ADD CONSTRAINT `FK_Teams_Departments_DepartmentId` FOREIGN KEY (`DepartmentId`) REFERENCES `Departments` (`Id`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261110120000_SecureRoleAccess', '8.0.13');

COMMIT;

