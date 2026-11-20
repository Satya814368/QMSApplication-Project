-- Run ONCE against qmsportaldb after taking a database backup.
-- Requires existing PortalUsers and DeliverableTargets tables.
CREATE TABLE IF NOT EXISTS TimeEntries (
 Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
 UserId BIGINT NOT NULL,
 TargetId BIGINT NULL,
 Kind VARCHAR(24) NOT NULL,
 Description VARCHAR(120) NOT NULL,
 StartUtc DATETIME(6) NOT NULL,
 EndUtc DATETIME(6) NULL,
 WorkMinutes INT NULL,
 Status VARCHAR(20) NOT NULL,
 CreatedById BIGINT NULL,
 CreatedUtc DATETIME(6) NOT NULL,
 ReviewedUtc DATETIME(6) NULL,
 ReviewedById BIGINT NULL,
 ReviewComment VARCHAR(500) NULL,
 INDEX IX_TimeEntries_UserId_StartUtc(UserId,StartUtc),
 INDEX IX_TimeEntries_TargetId(TargetId),
 CONSTRAINT FK_TimeEntries_PortalUsers_UserId FOREIGN KEY(UserId) REFERENCES PortalUsers(Id) ON DELETE RESTRICT,
 CONSTRAINT FK_TimeEntries_DeliverableTargets_TargetId FOREIGN KEY(TargetId) REFERENCES DeliverableTargets(Id) ON DELETE RESTRICT
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS TimeEntryAudits (
 Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
 EntryId BIGINT NOT NULL,
 ActorId BIGINT NOT NULL,
 Action VARCHAR(40) NOT NULL,
 Details VARCHAR(1000) NOT NULL,
 AtUtc DATETIME(6) NOT NULL,
 INDEX IX_TimeEntryAudits_EntryId(EntryId)
) ENGINE=InnoDB;
