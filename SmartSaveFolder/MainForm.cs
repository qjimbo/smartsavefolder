﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Management;
using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SmartSaveFolder
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }
        public ManagementEventWatcher watchForStart;
        public ManagementEventWatcher watchForEnd;
        public ManagementEventWatcher debugWatchForStart;
        public ManagementEventWatcher debugWatchForEnd;
        public string oldSaveGamePath;
        public string bakSaveGamePath;
        public string newSaveGamePath;
        public bool closing = false;
        public int runningId = 0;

        public void WriteToLog(string text, string notify = "")
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            this.Invoke((MethodInvoker)delegate()
            {
                textBox.AppendText("[" + date + "] " + text + Environment.NewLine);
                File.AppendAllText(Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),"log.txt"), "[" + date + "] " + text + Environment.NewLine);
                if (notify != "")
                    notifyIcon.ShowBalloonTip(1000, notify, text, ToolTipIcon.Info);
            });
        }


        private void MainForm_Load(object sender, EventArgs e)
        {
            // Try Icon
            notifyIcon.Visible = true;

            // Save Game Path
            oldSaveGamePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\HelloGames\\NMS";

            // Run at Startup
            var startupKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            chkStartup.Checked = startupKey.GetValue("SmartSaveFolder") != null;
            chkStartup.CheckedChanged += chkStartup_CheckedChanged;

            // Wait
            Watcher.CleanupOrphanedWatchers("NMS.exe");
            Watcher.CleanupOrphanedWatchers("XGOG Release_x64.exe");
            watchForStart = Watcher.WatchForProcessStart("NMS.exe", NoMansSkyStarted);
            watchForEnd = Watcher.WatchForProcessEnd("NMS.exe", NoMansSkyClosed);
            debugWatchForStart = Watcher.WatchForProcessStart("XGOG Release_x64.exe", NoMansSkyStarted); // DEBUG VERSION
            debugWatchForEnd = Watcher.WatchForProcessEnd("XGOG Release_x64.exe", NoMansSkyClosed); // DEBUG VERSION
            WriteToLog("Waiting for No Man's Sky to Start");

            // Verify Symbolic Link Permissions      
            VerifyPermissions(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\HelloGames");
        }

        private void NoMansSkyStarted(object sender, EventArrivedEventArgs e)
        {
            var processInfo = Watcher.GetProcessInfo(e);
            WriteToLog("No Man's Sky Started with Process ID [" + processInfo.ProcessId + "]");

            if (runningId != 0)
            {
                WriteToLog("Multiple instances of No Man's Sky Detected. Please only run a single No Man's Sky instance at a time.", "Multiple Instances Alert");
                return;
            }

            runningId = processInfo.ProcessId;

            WriteToLog("No Man's Sky Started (SmartSaveFolder cannot exit while NMS is running - DEBUG VERSION)");

            Process process = null;
            WriteToLog("Verify Process...");
            try
            {
                process = Process.GetProcessById(runningId);
                WriteToLog(" * => OK");
            }
            catch
            {
                // Not running
                WriteToLog(" * => Process not Found");
            }
            if (process == null)
            {
                WriteToLog("Process no longer found, not continuing and marking game as closed.");
                runningId = 0;
                return;
            }
            
            string path = processInfo.ExecutablePath;
            WriteToLog("Executable Path: " + path);

            newSaveGamePath = Path.Combine(Path.GetDirectoryName(path), "SAVEGAMES");

            // Move Existing Save Games Path
            bakSaveGamePath = oldSaveGamePath + ".bak";

            WriteToLog("Verifying if Old Save Game Path '" + oldSaveGamePath + "' exists...");
            var oldSaveGamePathExists = Directory.Exists(oldSaveGamePath);
            WriteToLog(" * => " + oldSaveGamePathExists);

            WriteToLog("Verifying if Backup Save Game Path '" + bakSaveGamePath + "' does not already exist...");
            var bakSaveGamePathExists = Directory.Exists(bakSaveGamePath);
            WriteToLog(" * => " + bakSaveGamePathExists);

            if (oldSaveGamePathExists && !bakSaveGamePathExists)
            {
                WriteToLog("Moving Existing Save Games Folder...");
                if(ForceMoveFolder.Execute(oldSaveGamePath, bakSaveGamePath))
                    WriteToLog(" * => OK");
                else
                {
                    WriteToLog(" * => Error");
                    WriteToLog("Unable to move existing Save Games Folder, please make sure you have no files or explorer windows open.","SmartSaveFolder Error");
                    return;
                }                
            }
            // Create Symbolic Link Path
            if (!Directory.Exists(newSaveGamePath))
                Directory.CreateDirectory(newSaveGamePath);

            var success = SymbolicLink.Create(oldSaveGamePath,newSaveGamePath);

            // Create DefaultUser folder and any Steam User Folders if Found
            if(Directory.Exists(bakSaveGamePath))
            {
                var bakFolderPaths = Directory.GetFileSystemEntries(bakSaveGamePath);
                foreach (var bakFolderPath in bakFolderPaths)
                {
                    string bakFolder = Path.GetFileName(bakFolderPath);
                    if (bakFolder.StartsWith("st_") || bakFolder == "DefaultUser")
                    {
                        var newSaveGamePathUser = newSaveGamePath + "\\" + bakFolder;
                        var newSaveGamePathUserCache = newSaveGamePathUser + "\\cache";
                        if (!Directory.Exists(newSaveGamePathUser))
                        {
                            WriteToLog("Created User Save Game Path " + bakFolder);
                            Directory.CreateDirectory(newSaveGamePathUser);
                        }
                        if (!Directory.Exists(newSaveGamePathUserCache))
                            Directory.CreateDirectory(newSaveGamePathUserCache);
                        
                    }
                }
            }
            this.Invoke((MethodInvoker)delegate()
            {
                buttonExit.Enabled = false;
                buttonCurrent.Enabled = true;
            });
            WriteToLog("Save Games Path set to " + newSaveGamePath, "No Man's Sky Detected");

        }

        private void NoMansSkyClosed(object sender, EventArrivedEventArgs e)
        {            
            // Verify ID of Closed Process
            var processInfo = Watcher.GetProcessInfo(e);
            WriteToLog("No Man's Sky Closed with Process ID [" + processInfo.ProcessId + "]");

            WriteToLog("Verifying Closed Process ID [" + processInfo.ProcessId + "] matches Running Process ID [" + runningId + "]...");
            if (runningId != processInfo.ProcessId)
            {
                WriteToLog("* => No Match");
                return;
            }
            else
            {
                WriteToLog("* => Match");
            }

            WriteToLog("No Man's Sky Closed");   
            runningId = 0;
            this.Invoke((MethodInvoker)delegate()
            {
                buttonExit.Enabled = true;
                buttonCurrent.Enabled = false;
            });
            SymbolicLink.Delete(oldSaveGamePath); // Remove Link
            if (Directory.Exists(bakSaveGamePath))
            {
                if (ForceMoveFolder.Execute(bakSaveGamePath, oldSaveGamePath)) // Restore original path instead of link
                    WriteToLog("Restored Save Games Folder");
                else
                {
                    WriteToLog("Unable to restore Save Games Folder, please make sure you have no files or explorer windows open.", "SmartSaveFolder Error");
                    return;
                }
            }
            WriteToLog("Save Games Path set to default location", "No Man's Sky Closed");
        }

        public void VerifyPermissions(string testFolder)
        {
            var hasPermission = false;
      
            // Test Permissions            
            string randomFolderNameA = Path.Combine(testFolder, Path.GetRandomFileName().Substring(0, 8));
            string randomFolderNameB = Path.Combine(testFolder, Path.GetRandomFileName().Substring(0, 8));

            try
            {
                // Create the random folder and symlink
                Directory.CreateDirectory(randomFolderNameB);
                SymbolicLink.Create(randomFolderNameA, randomFolderNameB);

                // Check if the folder exists
                bool folderAExists = Directory.Exists(randomFolderNameA);
                bool folderBExists = Directory.Exists(randomFolderNameA);

                // Delete the folder
                Directory.Delete(randomFolderNameB);
                SymbolicLink.Delete(randomFolderNameA);

                // Return whether the folder existed or not
                hasPermission = folderAExists && folderBExists;
            }
            catch (UnauthorizedAccessException)
            {
                // If we get an UnauthorizedAccessException, return false
                hasPermission = false;
            }

            // Process Result
            if (!hasPermission)
            {
                WriteToLog("Current User does not have permission to create Symbolic Links.");

                var result = MessageBox.Show("SmartSaveFolder needs to grant the current user permission to create Symbolic Links in order to redirect the Save Games folder. Click OK to perform this step.", "First Time Setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    var programPath = Assembly.GetEntryAssembly().Location;
                    var startInfo = new ProcessStartInfo(programPath)
                    {
                        Verb = "runas",
                        Arguments = "-grantprivilege " + Environment.UserName,
                        UseShellExecute = true,
                    };
                    WriteToLog("Please wait while permissions are changed.");
                    Process process = new Process();
                    process.StartInfo = startInfo;
                    process.Start();                    
                    process.WaitForExit();
                    WriteToLog("Permissions update complete, restart required.");

                    result = MessageBox.Show("A restart is required to apply the permission change. Click OK to restart now or Cancel to restart later.", "Restart Required", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

                    if (result == System.Windows.Forms.DialogResult.OK)
                        Process.Start("shutdown.exe", "-r -t 5");

                    WriteToLog("SmartSaveFolder will not work until system is rebooted.", "Permissions Required");
                    return;
                }
                else
                {
                    WriteToLog("SmartSaveFolder will not work until permissions are changed.", "Permissions Required");
                    return;
                }
                
            }
        }


        private void buttonOriginal_Click(object sender, EventArgs e)
        {
            if (runningId != 0)
                if(Directory.Exists(bakSaveGamePath))
                    Process.Start(bakSaveGamePath);
            else
               if(Directory.Exists(oldSaveGamePath))
                    Process.Start(oldSaveGamePath);
        }

        private void buttonCurrent_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(newSaveGamePath))
                Process.Start(newSaveGamePath);
        }

        private void buttonExit_Click(object sender, EventArgs e)
        {
            closing = true;
            watchForStart.Stop();
            watchForStart.Dispose();
            watchForEnd.Stop();
            watchForEnd.Dispose();
            debugWatchForStart.Stop();
            debugWatchForStart.Dispose();
            debugWatchForEnd.Stop();
            debugWatchForEnd.Dispose();
            Application.Exit();
        }

        private void notifyIcon_Click(object sender, EventArgs e)
        {
            
            this.Show();
            WindowState = FormWindowState.Normal;
            this.BringToFront();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                notifyIcon.ShowBalloonTip(1000, "SmartSaveFolder is still running", "Click on the tray icon and click the exit button to close", ToolTipIcon.Info);
                Hide();
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (closing)
                return;
            notifyIcon.ShowBalloonTip(1000, "SmartSaveFolder is still running", "Click on the tray icon and click the exit button to close", ToolTipIcon.Info);
            Hide();
            e.Cancel = true;
        }

        private void pictureBoxLink_Click(object sender, EventArgs e)
        {
            Process.Start("https://nomansskyretro.com");
        }

        private void pictureBoxLink_MouseHover(object sender, EventArgs e)
        {
            ToolTip tt = new ToolTip();
            tt.SetToolTip(this.pictureBoxLink, "Visit NoMansSkyRetro.com");
        }

        private void chkStartup_CheckedChanged(object sender, EventArgs e)
        {
            var startupKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            try
            {
                if (chkStartup.Checked)
                {
                    // Add the program to the registry to run at startup with -minimized parameter
                    startupKey.SetValue("SmartSaveFolder", Application.ExecutablePath + " -minimized");
                    WriteToLog("SmartSaveFolder will launch at Windows Startup.");
                }
                else
                {
                    // Remove the program from the registry                
                    startupKey.DeleteValue("SmartSaveFolder", false);
                    WriteToLog("SmartSaveFolder will no longer launch at Windows Startup.");
                }
            }
            catch
            {
                // Error updating registry
                WriteToLog("SmartSaveFolder could not update registry.");
            }
        }


    }
}
