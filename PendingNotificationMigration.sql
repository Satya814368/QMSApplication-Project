START TRANSACTION;

ALTER TABLE `Notifications` DROP COLUMN `IsMandatory`;

ALTER TABLE `Notifications` DROP COLUMN `Priority`;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261118110000_RemoveNotificationPriorityAndMandatory', '8.0.13');

COMMIT;

