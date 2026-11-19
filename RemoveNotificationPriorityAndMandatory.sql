-- Stop the application and back up the database BEFORE running this script.
-- Run ONCE as database administrator. Stop on any error.
-- Removes only the obsolete Priority and IsMandatory columns; acknowledgement records are preserved.
USE qmsportaldb;

ALTER TABLE `Notifications` DROP COLUMN `IsMandatory`;

ALTER TABLE `Notifications` DROP COLUMN `Priority`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`,`ProductVersion`) VALUES ('20261118110000_RemoveNotificationPriorityAndMandatory','8.0.13');
