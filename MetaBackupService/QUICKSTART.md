# MetaBackup Service - Quick Start Guide

## 5-Minute Setup

### Step 1: Build the Service

```bash
# Open Developer Command Prompt for Visual Studio
cd C:\Users\Metasoft\source\repos\metabackup\MetaBackupService
dotnet build -c Release
```

### Step 2: Create Config Directory

```bash
mkdir "%PROGRAMDATA%\MetaBackup"
```

### Step 3: Create Initial Config File

Create `%PROGRAMDATA%\MetaBackup\config.json`:

```json
{
  "username": "admin",
  "password_hash": "",
  "tasks": [
    {
      "id": "backup-1",
      "name": "Test Backup",
      "type": "backup",
      "enabled": true,
      "source": "C:\\Users\\YourName\\Documents",
      "dest": "D:\\Backups",
      "times": ["09:00"],
      "days": "*",
      "full": true,
      "keep_last": 3,
      "username": "admin"
    }
  ],
  "telegram": {
    "bot_token_enc": "",
    "chat_ids": [],
    "message_filter": "failure_only"
  },
  "watchdog": {
    "services": [],
    "manually_stopped": []
  }
}
```

Replace:
- `C:\Users\YourName\Documents` ? your source folder
- `D:\Backups` ? your backup destination
- `09:00` ? preferred backup time (24-hour format)

### Step 4: Install Service (As Administrator)

```bash
# Open Command Prompt as Administrator
cd C:\Users\Metasoft\source\repos\metabackup\MetaBackupService\bin\Release

sc.exe create MetaBackupService binPath= "%cd%\MetaBackupService.exe"
sc.exe start MetaBackupService
```

### Step 5: Verify Installation

```bash
# Check service status
sc.exe query MetaBackupService

# Should show:
# STATUS        : 4  RUNNING
```

### Step 6: Monitor Logs

```bash
# Watch log file in real-time (PowerShell)
Get-Content "%PROGRAMDATA%\MetaBackup\Service.log" -Wait -Tail 20

# Or just view file
type "%PROGRAMDATA%\MetaBackup\Service.log"
```

### Step 7: Test Manually (Optional)

Instead of waiting for scheduled time, create a test by:

1. Edit config: change time to 1 minute from now
2. Save config
3. Service will reload and run backup at that time
4. Check `D:\Backups\{Year}\{MM_Month}\Yedek_*\` folder

---

## Common Configurations

### Hourly Incremental Backups

```json
{
  "name": "Hourly Backups",
  "type": "backup",
  "source": "C:\\Data",
  "dest": "D:\\Backups",
  "times": ["09:00", "10:00", "11:00", "12:00", "13:00", "14:00", "15:00", "16:00", "17:00"],
  "days": "pzt,sal,çar,per,cum",
  "full": false
}
```

### Daily Full Backup at 2 AM

```json
{
  "name": "Nightly Full Backup",
  "type": "backup",
  "source": "C:\\Data",
  "dest": "D:\\Backups",
  "times": ["02:00"],
  "days": "*",
  "full": true,
  "keep_last": 7
}
```

### Weekly Cleanup (Keep Last 90 Days)

```json
{
  "name": "Weekly Cleanup",
  "type": "cleaning",
  "source": "D:\\Backups\\Archive",
  "times": ["23:00"],
  "days": "*",
  "delete_older_than_days": 90
}
```

### Multiple Tasks

```json
{
  "tasks": [
    {
      "id": "1",
      "name": "Full Backup Sunday",
      "type": "backup",
      "source": "C:\\Data",
      "dest": "D:\\Backups",
      "times": ["02:00"],
      "days": "paz",
      "full": true,
      "keep_last": 5
    },
    {
      "id": "2",
      "name": "Incremental Backups",
      "type": "backup",
      "source": "C:\\Data",
      "dest": "D:\\Backups",
      "times": ["02:00"],
      "days": "pzt,sal,çar,per,cum",
      "full": false
    },
    {
      "id": "3",
      "name": "Cleanup Archive",
      "type": "cleaning",
      "source": "D:\\Backups\\Archive",
      "times": ["23:00"],
      "days": "*",
      "delete_older_than_days": 180
    }
  ]
}
```

---

## Day Abbreviations

### Turkish
- `pzt` = Pazartesi (Monday)
- `sal` = Salý (Tuesday)
- `çar` = Çarþamba (Wednesday)
- `per` = Perþembe (Thursday)
- `cum` = Cuma (Friday)
- `cts` = Cumartesi (Saturday)
- `paz` = Pazar (Sunday)

### English
- `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday`, `Saturday`, `Sunday`

### Every Day
- `*` or leave empty

---

## Troubleshooting

### Service won't start

1. Check config syntax:
   ```bash
   type "%PROGRAMDATA%\MetaBackup\config.json"
   ```

2. Check permissions:
   ```bash
   # Service runs as LocalSystem, must access source/dest
   icacls "D:\Backups" /grant "NT AUTHORITY\SYSTEM:F"
   ```

3. Check logs:
   ```bash
   type "%PROGRAMDATA%\MetaBackup\Service.log"
   ```

### Backup not running

1. Verify task is enabled:
   ```json
   "enabled": true
   ```

2. Check time format (24-hour):
   ```json
   "times": ["14:30"]  // NOT "2:30 PM"
   ```

3. Verify day matches today:
   ```json
   "days": "*"  // Always runs, or specify "pzt" etc
   ```

4. Confirm service is running:
   ```bash
   sc.exe query MetaBackupService
   ```

### Incremental backup keeps full backing up

1. Check manifest exists:
   ```bash
   dir "%USERPROFILE%\Backups\2024\01_January\"
   # Should see _manifest.json file
   ```

2. First backup is always full - normal!

3. On second and subsequent runs, should be incremental

### Service taking too much CPU

1. Too many files being scanned
   - Consider scheduling at off-hours
   - Split large folders into multiple tasks

2. Unresponsive system
   - Service runs tasks in ThreadPool
   - Shouldn't block, but large backups take time

---

## Uninstall Service

```bash
# As Administrator
sc.exe stop MetaBackupService
sc.exe delete MetaBackupService

# Verify
sc.exe query MetaBackupService
# Should say: ERROR: Service 'MetaBackupService' does not exist
```

---

## Next Steps

1. **Add more tasks** in config.json
2. **Monitor logs** regularly
3. **Test restore** from backups
4. **Adjust schedule** as needed
5. **Enable Telegram** (Phase 2) for notifications
6. **Add SFTP/FTP** (Phase 2) for remote backups

---

## Support

Check:
- Service log: `%PROGRAMDATA%\MetaBackup\Service.log`
- Event Viewer: Event ID in System logs
- GitHub Issues: Report problems

---

**Installation Complete!** ??

Your backups are now automated and will run on the schedule you defined.
