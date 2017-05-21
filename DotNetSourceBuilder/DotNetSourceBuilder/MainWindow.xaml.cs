using System;
using System.Windows;
using System.Management.Automation;
using Microsoft.WindowsAPICodePack.Dialogs;
using Microsoft.Win32;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace DotNetSourceBuilder
{
    public partial class MainWindow : Window
    {
        private readonly int timeout;
        private readonly string powershellScriptPath;
        private readonly string cakeScriptPath;
        private TimeSpan timeoutTS;
        private const char ExtensionDelimer = ';';

        public MainWindow()
        {
            timeout = Properties.Settings.Default.ExecutionTimeOutInSeconds;
            powershellScriptPath = Properties.Settings.Default.PowershellScriptPath;
            cakeScriptPath = Properties.Settings.Default.CakeScriptPath;
            timeoutTS = new TimeSpan(0, 0, 0, timeout);
            InitializeComponent();

            EnableDisableCopy(false);
        }

        #region Buttons & Checkboxes
        private void btnBuild_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(txtSolutionPath.Text))
            {
                AccessGrant();
                txtOutput.Clear();
                PerformBuild(txtSolutionPath.Text);
            }
        }
        private void btnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
                txtSolutionPath.Text = openFileDialog.FileName;
        }

        private void btnOpenDestinationBinPath_Click(object sender, RoutedEventArgs e)
        {
            var openFolderDialog = new CommonOpenFileDialog();
            openFolderDialog.IsFolderPicker = true;
            var result = openFolderDialog.ShowDialog();

            if (result == CommonFileDialogResult.Ok)
            {
                txtDestinationBinPath.Text = openFolderDialog.FileName;
            }
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            EnableDisableCopy(true);
        }

        private void CheckBox_UnChecked(object sender, RoutedEventArgs e)
        {
            EnableDisableCopy(false);
        }
        #endregion

        #region Builder
        private void AccessGrant()
        {
            using (System.Management.Automation.PowerShell PowerShellInstance = System.Management.Automation.PowerShell.Create())
            {
                PowerShellInstance.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned");
                var results = PowerShellInstance.Invoke();
            }
        }

        private void CopyFilesToDestinationPath()
        {
            try
            {
                if (chkCopyBuildBin.IsChecked.HasValue ? chkCopyBuildBin.IsChecked.Value : false && !string.IsNullOrEmpty(txtDestinationBinPath.Text))
                {
                    txtOutput.AppendText("Proceed to Copy Bin Files to Destination Path");
                    txtOutput.AppendText(Environment.NewLine);

                    if (File.GetAttributes(txtDestinationBinPath.Text).HasFlag(FileAttributes.Directory))
                    {
                        var solutionDirectoryPath = Path.GetDirectoryName(txtSolutionPath.Text);

                        string[] binDirectories = Directory.GetDirectories(solutionDirectoryPath, "bin", SearchOption.AllDirectories);

                        var listofFilePath = new List<string>();

                        foreach (var bin in binDirectories)
                        {
                            if (!string.IsNullOrEmpty(txtExtensionPattern.Text))
                            {
                                foreach (var delimiter in txtExtensionPattern.Text.Split(ExtensionDelimer))
                                {
                                    listofFilePath.AddRange(Directory.GetFiles(bin, delimiter, System.IO.SearchOption.AllDirectories).ToList());
                                }
                            }

                            foreach (var filePath in listofFilePath)
                            {

                                Dispatcher.Invoke((Action)(() =>
                                {
                                    txtOutput.AppendText("Copy From :" + filePath);
                                    txtOutput.AppendText(Environment.NewLine);
                                    var destinationPath = txtDestinationBinPath.Text + "\\" + Path.GetFileName(filePath);
                                    txtOutput.AppendText("Copy To :" + destinationPath);
                                    txtOutput.AppendText(Environment.NewLine);

                                    File.Copy(filePath, destinationPath, true);
                                }));
                            }
                        }

                        txtOutput.AppendText("----- Copy Success ----- ^_^ -----");
                        txtOutput.AppendText(Environment.NewLine);

                    }
                    else
                    {

                        txtOutput.AppendText("Warning : Destination Path is not a valid directory");
                        txtOutput.AppendText(Environment.NewLine);
                        txtOutput.AppendText("----- Copy Fail ----- ^_^ -----");
                        txtOutput.AppendText(Environment.NewLine);
                    }
                }

            }
            catch (Exception ex)
            {
                txtOutput.AppendText("Warning : " + ex.Message);
                txtOutput.AppendText(Environment.NewLine);

                txtOutput.AppendText("----- Copy Fail ----- ^_^ -----");
                txtOutput.AppendText(Environment.NewLine);

            }
        }

        private void PerformBuild(string path)
        {
            ThreadPool.QueueUserWorkItem((o) =>
            {
                using (PowerShell PowerShellInstance = PowerShell.Create())
                {
                    PowerShellInstance.AddScript(@"" + powershellScriptPath + @" -Script " + cakeScriptPath
                    + " -solutionPath=\"" + path + "\"");


                    PSDataCollection<PSObject> outputCollection = new PSDataCollection<PSObject>();
                    outputCollection.DataAdded += outputCollection_DataAdded;

                    PowerShellInstance.Streams.Error.DataAdded += Error_DataAdded;

                    DateTime startTime = DateTime.Now;

                    IAsyncResult result = PowerShellInstance.BeginInvoke<PSObject, PSObject>(null, outputCollection);

                    while (result.IsCompleted == false)
                    {
                        Thread.Sleep(1000);

                        TimeSpan elasped = DateTime.Now.Subtract(startTime);
                        if (elasped > timeoutTS)
                        {
                            Dispatcher.Invoke((Action)(() =>
                            {
                                txtOutput.AppendText(Environment.NewLine);
                                txtOutput.AppendText(Environment.NewLine);
                                txtOutput.AppendText(Environment.NewLine);
                                txtOutput.AppendText("Building script taking too long");
                                txtOutput.ScrollToEnd();
                            }));
                            break;
                        }
                    }

                    if (result.IsCompleted)
                    {
                        Dispatcher.Invoke((Action)(() =>
                        {
                            txtOutput.AppendText(Environment.NewLine);
                            txtOutput.AppendText("--------------------------------------------------");
                            txtOutput.AppendText(Environment.NewLine);
                            txtOutput.AppendText("----- Build Success ----- ^_^ -----");
                            txtOutput.AppendText(Environment.NewLine);
                            txtOutput.ScrollToEnd();

                            CopyFilesToDestinationPath();
                        }));
                    }
                }
            });
        }

        private void outputCollection_DataAdded(object sender, DataAddedEventArgs e)
        {
            var outputList = (PSDataCollection<PSObject>)sender;

            if (outputList.Count > 0)
            {
                Dispatcher.Invoke((Action)(() =>
                {
                    txtOutput.AppendText(outputList[outputList.Count - 1].BaseObject.ToString());
                    txtOutput.AppendText(Environment.NewLine);
                    txtOutput.ScrollToEnd();
                }

                 ));
            }
        }

        private void Error_DataAdded(object sender, DataAddedEventArgs e)
        {
            var outputList = (PSDataCollection<ErrorRecord>)sender;

            Dispatcher.Invoke((Action)(() =>
            {
                txtOutput.AppendText(outputList[outputList.Count - 1].Exception.Message);
                txtOutput.AppendText(Environment.NewLine);
                txtOutput.ScrollToEnd();
            }

               ));
        }

        private void EnableDisableCopy(bool Enable)
        {
            txtDestinationBinPath.IsEnabled = Enable;
            lblBuildDestinationFolder.IsEnabled = Enable;
            txtExtensionPattern.IsEnabled = Enable;
            btnOpenDestinationBinPath.IsEnabled = Enable;
            lblExtensionPatternToCopy.IsEnabled = Enable;
        } 
        #endregion
    }

}
