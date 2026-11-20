-- Run once against the same database configured in ConnectionStrings:QMSPortalDb.
-- Back up your database before installation. Do not apply a separate EF migration creating these same tables.
CREATE TABLE IF NOT EXISTS `UserPreferences` (
 `Id` bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
 `UserId` bigint NOT NULL,
 `Theme` varchar(16) NOT NULL DEFAULT 'Light',
 `Language` varchar(16) NOT NULL DEFAULT 'English',
 `DateFormat` varchar(24) NOT NULL DEFAULT 'dd-MM-yyyy',
 `TaskAlerts` tinyint(1) NOT NULL DEFAULT 1,
 `ApprovalAlerts` tinyint(1) NOT NULL DEFAULT 1,
 `EmailAlerts` tinyint(1) NOT NULL DEFAULT 1,
 UNIQUE KEY `IX_UserPreferences_UserId` (`UserId`),
 CONSTRAINT `FK_UserPreferences_PortalUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `PortalUsers` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS `WorkspaceSettings` (
 `Key` varchar(80) NOT NULL PRIMARY KEY,
 `Value` varchar(500) NOT NULL,
 `UpdatedUtc` datetime(6) NOT NULL,
 `UpdatedById` bigint NOT NULL
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS `SettingsAuditEvents` (
 `Id` bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,
 `ActorId` bigint NOT NULL,
 `SettingKey` varchar(80) NOT NULL,
 `PreviousValue` varchar(500) NULL,
 `NewValue` varchar(500) NULL,
 `AtUtc` datetime(6) NOT NULL,
 KEY `IX_SettingsAuditEvents_AtUtc` (`AtUtc`)
) ENGINE=InnoDB;
