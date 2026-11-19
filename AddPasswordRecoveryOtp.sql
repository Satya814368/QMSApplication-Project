-- Run the WHOLE script on the MySQL server in Workbench as a database administrator.
-- Back up qmsportaldb first. Stop on any error. This script also upgrades the earlier manual OTP table.
USE qmsportaldb;

CREATE TABLE IF NOT EXISTS `PasswordRecoveryChallenges` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `UserId` bigint NOT NULL,
    `AccountSecurityStamp` char(36) COLLATE ascii_general_ci NOT NULL,
    `Version` char(36) COLLATE ascii_general_ci NOT NULL,
    `OwnerHash` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `CodeHash` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAtUtc` datetime(6) NOT NULL,
    `ExpiresAtUtc` datetime(6) NOT NULL,
    `LastSentAtUtc` datetime(6) NOT NULL,
    `AttemptCount` int NOT NULL,
    `IsConsumed` tinyint(1) NOT NULL,
    CONSTRAINT `PK_PasswordRecoveryChallenges` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_PasswordRecoveryChallenges_PortalUsers_UserId` FOREIGN KEY (`UserId`) REFERENCES `PortalUsers` (`Id`) ON DELETE RESTRICT,
    INDEX `IX_PasswordRecoveryChallenges_UserId_CreatedAtUtc` (`UserId`, `CreatedAtUtc`),
    INDEX `IX_PasswordRecoveryChallenges_LastSentAtUtc` (`LastSentAtUtc`)
) CHARACTER SET=utf8mb4;

-- Existing manual installations may not have the two new security fields.
SET @recovery_sql = IF((SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='PasswordRecoveryChallenges' AND COLUMN_NAME='AccountSecurityStamp')=0,
    'ALTER TABLE `PasswordRecoveryChallenges` ADD `AccountSecurityStamp` char(36) COLLATE ascii_general_ci NOT NULL DEFAULT ''00000000-0000-0000-0000-000000000000''', 'SELECT 1');
PREPARE recovery_stmt FROM @recovery_sql;
EXECUTE recovery_stmt;
DEALLOCATE PREPARE recovery_stmt;
SET @recovery_sql = IF((SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='PasswordRecoveryChallenges' AND COLUMN_NAME='Version')=0,
    'ALTER TABLE `PasswordRecoveryChallenges` ADD `Version` char(36) COLLATE ascii_general_ci NOT NULL DEFAULT ''00000000-0000-0000-0000-000000000000''', 'SELECT 1');
PREPARE recovery_stmt FROM @recovery_sql;
EXECUTE recovery_stmt;
DEALLOCATE PREPARE recovery_stmt;

-- Earlier unbound codes must be requested again after the upgrade.
UPDATE `PasswordRecoveryChallenges` SET `IsConsumed`=1, `CodeHash`=''
WHERE `AccountSecurityStamp`='00000000-0000-0000-0000-000000000000' AND `IsConsumed`=0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`,`ProductVersion`)
SELECT '20261119064043_AddPasswordRecoveryOtp','8.0.13'
WHERE NOT EXISTS (SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId`='20261119064043_AddPasswordRecoveryOtp');

-- Verification: the first result should list the table, the next 11 columns, and the last one migration row.
SHOW TABLES LIKE 'PasswordRecoveryChallenges';
SHOW COLUMNS FROM `PasswordRecoveryChallenges`;
SELECT `MigrationId` FROM `__EFMigrationsHistory` WHERE `MigrationId`='20261119064043_AddPasswordRecoveryOtp';
