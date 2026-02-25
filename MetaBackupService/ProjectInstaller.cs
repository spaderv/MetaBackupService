using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration.Install;
using System.ServiceProcess;

namespace MetaBackupService
{
    /// <summary>
    /// Installer class for MetaBackup Windows Service
    /// Note: RunInstaller attribute commented out due to System.Configuration.Install assembly issues
    /// Service can still be installed manually using: sc.exe create MetaBackupService binPath= "C:\path\to\MetaBackupService.exe"
    /// </summary>
    // [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        private ServiceProcessInstaller serviceProcessInstaller;
        private ServiceInstaller serviceInstaller;

        public ProjectInstaller()
        {
            // Run as Local System
            serviceProcessInstaller = new ServiceProcessInstaller();
            serviceProcessInstaller.Account = ServiceAccount.LocalSystem;

            // Service configuration
            serviceInstaller = new ServiceInstaller();
            serviceInstaller.ServiceName = "MetaBackupService";
            serviceInstaller.DisplayName = "MetaBackup Scheduler Service";
            serviceInstaller.StartType = ServiceStartMode.Automatic;

            Installers.Add(serviceProcessInstaller);
            Installers.Add(serviceInstaller);
        }
    }
}
