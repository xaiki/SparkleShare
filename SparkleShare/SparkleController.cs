//   SparkleShare, a collaboration and sharing tool.
//   Copyright (C) 2010  Hylke Bons <hylkebons@gmail.com>
//
//   This program is free software: you can redistribute it and/or modify
//   it under the terms of the GNU General Public License as published by
//   the Free Software Foundation, either version 3 of the License, or
//   (at your option) any later version.
//
//   This program is distributed in the hope that it will be useful,
//   but WITHOUT ANY WARRANTY; without even the implied warranty of
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//   GNU General Public License for more details.
//
//   You should have received a copy of the GNU General Public License
//   along with this program. If not, see <http://www.gnu.org/licenses/>.


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

using System.Globalization;
using System.Collections;

using Mono.Unix;
using SparkleLib;

namespace SparkleShare {

    public abstract class SparkleController {

        public List <SparkleRepoBase> Repositories;
        public string FolderSize;
        public readonly string SparklePath = SparklePaths.SparklePath;

        public event OnQuitWhileSyncingEventHandler OnQuitWhileSyncing;
        public delegate void OnQuitWhileSyncingEventHandler ();

        public event FolderFetchedEventHandler FolderFetched;
        public delegate void FolderFetchedEventHandler ();
        
        public event FolderFetchErrorEventHandler FolderFetchError;
        public delegate void FolderFetchErrorEventHandler ();
        
        public event FolderListChangedEventHandler FolderListChanged;
        public delegate void FolderListChangedEventHandler ();

        public event FolderSizeChangedEventHandler FolderSizeChanged;
        public delegate void FolderSizeChangedEventHandler (string folder_size);
        
        public event AvatarFetchedEventHandler AvatarFetched;
        public delegate void AvatarFetchedEventHandler ();

        public event OnIdleEventHandler OnIdle;
        public delegate void OnIdleEventHandler ();

        public event OnSyncingEventHandler OnSyncing;
        public delegate void OnSyncingEventHandler ();

        public event OnErrorEventHandler OnError;
        public delegate void OnErrorEventHandler ();

        public event OnInvitationEventHandler OnInvitation;
        public delegate void OnInvitationEventHandler (string server, string folder, string token);

        public event ConflictNotificationRaisedEventHandler ConflictNotificationRaised;
        public delegate void ConflictNotificationRaisedEventHandler ();

        public event NotificationRaisedEventHandler NotificationRaised;
        public delegate void NotificationRaisedEventHandler (string user_name, string user_email,
                                                             string message, string repository_path);

        
        // Short alias for the translations
        public static string _ (string s)
        {
            return Catalog.GetString (s);
        }


        public SparkleController () { }

        public virtual void Initialize ()
        {
            InstallLauncher ();
            EnableSystemAutostart ();

            // Create the SparkleShare folder and add it to the bookmarks
            if (CreateSparkleShareFolder ())
                AddToBookmarks ();

            FolderSize = GetFolderSize ();

            // TODO: Legacy. Remove at some later point
            string old_global_config_file_path = Path.Combine (SparklePaths.SparkleConfigPath, "config");
            if (File.Exists (old_global_config_file_path))
                MigrateConfig ();

            if (FirstRun)
                SparkleConfig.DefaultConfig.SetConfigOption ("notifications", bool.TrueString);
            else
                AddKey ();

            // Watch the SparkleShare folder
            FileSystemWatcher watcher = new FileSystemWatcher (SparklePaths.SparklePath) {
                IncludeSubdirectories = false,
                EnableRaisingEvents   = true,
                Filter                = "*"
            };

            // Remove the repository when a delete event occurs
            watcher.Deleted += delegate (object o, FileSystemEventArgs args) {
                RemoveRepository (args.FullPath);
                SparkleConfig.DefaultConfig.RemoveFolder (Path.GetFileName (args.Name));

                if (FolderListChanged != null)
                    FolderListChanged ();

                FolderSize = GetFolderSize ();

                if (FolderSizeChanged != null)
                    FolderSizeChanged (FolderSize);
            };


            watcher.Created += delegate (object o, FileSystemEventArgs args) {

                // Handle invitations when the user saves an
                // invitation into the SparkleShare folder
                if (args.Name.EndsWith (".sparkle") && !FirstRun) {
                    XmlDocument xml_doc = new XmlDocument (); 
                    xml_doc.Load (args.Name);

                    string server = xml_doc.GetElementsByTagName ("server") [0].InnerText;
                    string folder = xml_doc.GetElementsByTagName ("folder") [0].InnerText;
                    string token  = xml_doc.GetElementsByTagName ("token") [0].InnerText;
            
                    // FIXME: this is broken :\
                    if (OnInvitation != null)
                        OnInvitation (server, folder, token);
                }
            };

            new Thread (new ThreadStart (PopulateRepositories)).Start ();
        }


        public bool FirstRun {
            get {
                return SparkleConfig.DefaultConfig.UserEmail.Equals ("Unknown");
            }
        }


         private void MigrateConfig ()
         {
            string old_global_config_file_path = Path.Combine (SparklePaths.SparkleConfigPath, "config");

            StreamReader reader = new StreamReader (old_global_config_file_path);
            string global_config_file = reader.ReadToEnd ();
            reader.Close ();

            Regex regex = new Regex (@"name.+= (.+)");
            Match match = regex.Match (global_config_file);

            string user_name = match.Groups [1].Value;

            regex = new Regex (@"email.+= (.+)");
            match = regex.Match (global_config_file);

            string user_email = match.Groups [1].Value;

            SparkleConfig.DefaultConfig.UserName  = user_name;
            SparkleConfig.DefaultConfig.UserEmail = user_email;

            File.Delete (old_global_config_file_path);
        }


        // Uploads the user's public key to the server
        public bool AcceptInvitation (string server, string folder, string token)
        {
            // The location of the user's public key for SparkleShare
            string public_key_file_path = SparkleHelpers.CombineMore (SparklePaths.HomePath, ".ssh",
                "sparkleshare." + UserEmail + ".key.pub");

            if (!File.Exists (public_key_file_path))
                return false;

            StreamReader reader = new StreamReader (public_key_file_path);
            string public_key = reader.ReadToEnd ();
            reader.Close ();

            string url = "https://" + server + "/?folder=" + folder +
                         "&token=" + token + "&pubkey=" + public_key;

            SparkleHelpers.DebugInfo ("WebRequest", url);

            HttpWebRequest request   = (HttpWebRequest) WebRequest.Create (url);
            HttpWebResponse response = (HttpWebResponse) request.GetResponse();

            if (response.StatusCode == HttpStatusCode.OK) {
                response.Close ();
                return true;

            } else {
                response.Close ();
                return false;
            }
        }


        public List<string> Folders {
            get {
                List<string> folders = SparkleConfig.DefaultConfig.Folders;
                folders.Sort ();
                return folders;
            }
        }


        public List<string> UnsyncedFolders {
            get {
                List<string> unsynced_folders = new List<string> ();

                foreach (SparkleRepoBase repo in Repositories) {
                    if (repo.HasUnsyncedChanges)
                        unsynced_folders.Add (repo.Name);
                 }

                return unsynced_folders;
            }
        }
        

        public List<SparkleChangeSet> GetLog ()
        {
            List<SparkleChangeSet> list = new List<SparkleChangeSet> ();

            foreach (SparkleRepoBase repo in Repositories)
                list.AddRange (repo.GetChangeSets (50));

            list.Sort ((x, y) => (x.Timestamp.CompareTo (y.Timestamp)));
            list.Reverse ();

            if (list.Count > 100)
                return list.GetRange (0, 100);
            else
                return list.GetRange (0, list.Count);
        }


        public List<SparkleChangeSet> GetLog (string name)
        {
            if (name == null)
                return GetLog ();

            string path = Path.Combine (SparklePaths.SparklePath, name);
            int log_size = 50;
            
            foreach (SparkleRepoBase repo in Repositories) {
                if (repo.LocalPath.Equals (path))            
                    return repo.GetChangeSets (log_size);
            }

            return null;
        }
        
        
        public abstract string EventLogHTML { get; }
        public abstract string DayEntryHTML { get; }
        public abstract string EventEntryHTML { get; }
        
        
        public string GetHTMLLog (List<SparkleChangeSet> change_sets)
        {
            List <ActivityDay> activity_days = new List <ActivityDay> ();
            List<string> emails = new List<string> ();

            change_sets.Sort ((x, y) => (x.Timestamp.CompareTo (y.Timestamp)));
            change_sets.Reverse ();

            if (change_sets.Count == 0)
                return null;

            foreach (SparkleChangeSet change_set in change_sets) {
                if (!emails.Contains (change_set.UserEmail))
                    emails.Add (change_set.UserEmail);

                bool change_set_inserted = false;
                foreach (ActivityDay stored_activity_day in activity_days) {
                    if (stored_activity_day.DateTime.Year  == change_set.Timestamp.Year &&
                        stored_activity_day.DateTime.Month == change_set.Timestamp.Month &&
                        stored_activity_day.DateTime.Day   == change_set.Timestamp.Day) {

                        stored_activity_day.Add (change_set);
                        change_set_inserted = true;
                        break;
                    }
                }

                if (!change_set_inserted) {
                    ActivityDay activity_day = new ActivityDay (change_set.Timestamp);
                    activity_day.Add (change_set);
                    activity_days.Add (activity_day);
                }
            }

            new Thread (new ThreadStart (delegate {
                FetchAvatars (emails, 48);
            })).Start ();

            string event_log_html   = EventLogHTML;
            string day_entry_html   = DayEntryHTML;
            string event_entry_html = EventEntryHTML;
            string event_log        = "";

            foreach (ActivityDay activity_day in activity_days) {
                string event_entries = "";

                foreach (SparkleChangeSet change_set in activity_day) {
                    string event_entry = "<dl>";
                    if (change_set.Notes.Count > 0)
                    Console.WriteLine (change_set.ToJSON ());

                    if (change_set.IsMerge) {
                        event_entry += "<dd>Did something magical</dd>";

                    } else {
                        if (change_set.Edited.Count > 0) {
                            foreach (string file_path in change_set.Edited) {
                                string absolute_file_path = SparkleHelpers.CombineMore (SparklePaths.SparklePath,
                                    change_set.Folder, file_path);
                                
                                if (File.Exists (absolute_file_path))
                                    event_entry += "<dd class='document edited'><a href='" + absolute_file_path + "'>" + file_path + "</a></dd>";
                                else
                                    event_entry += "<dd class='document edited'>" + file_path + "</dd>";
                            }
                        }
    
                        if (change_set.Added.Count > 0) {
                            foreach (string file_path in change_set.Added) {
                                string absolute_file_path = SparkleHelpers.CombineMore (SparklePaths.SparklePath,
                                    change_set.Folder, file_path);
                                
                                if (File.Exists (absolute_file_path))
                                    event_entry += "<dd class='document added'><a href='" + absolute_file_path + "'>" + file_path + "</a></dd>";
                                else
                                    event_entry += "<dd class='document added'>" + file_path + "</dd>";
                            }
                        }
    
                        if (change_set.Deleted.Count > 0) {
                            foreach (string file_path in change_set.Deleted) {
                                string absolute_file_path = SparkleHelpers.CombineMore (SparklePaths.SparklePath,
                                    change_set.Folder, file_path);
                                
                                if (File.Exists (absolute_file_path))
                                    event_entry += "<dd class='document deleted'><a href='" + absolute_file_path + "'>" + file_path + "</a></dd>";
                                else
                                    event_entry += "<dd class='document deleted'>" + file_path + "</dd>";
                            }
                        }

                        if (change_set.MovedFrom.Count > 0) {
                            int i = 0;
                            foreach (string file_path in change_set.MovedFrom) {
                                string to_file_path = change_set.MovedTo [i];
                                string absolute_file_path = SparkleHelpers.CombineMore (SparklePaths.SparklePath,
                                    change_set.Folder, file_path);
                                string absolute_to_file_path = SparkleHelpers.CombineMore (SparklePaths.SparklePath,
                                    change_set.Folder, to_file_path);

                                if (File.Exists (absolute_file_path))
                                    event_entry += "<dd class='document moved'><a href='" + absolute_file_path + "'>" + file_path + "</a><br/>";
                                else
                                    event_entry += "<dd class='document moved'>" + file_path + "<br/>";

                                if (File.Exists (absolute_to_file_path))
                                    event_entry += "<a href='" + absolute_to_file_path + "'>" + to_file_path + "</a></dd>";
                                else
                                    event_entry += to_file_path + "</dd>";

                                i++;
                            }
                        }
                    }

                    string comments = "";
                    comments = "<div class=\"comments\">";

                    if (change_set.Notes != null) {
                        change_set.Notes.Sort ((x, y) => (x.Timestamp.CompareTo (y.Timestamp)));

                        foreach (SparkleNote note in change_set.Notes) {
                            comments += "<div class=\"comment-text\">" +
                                        "<p class=\"comment-author\"" +
                                        " style=\"background-image: url('file://" + GetAvatar (note.UserEmail, 48) + "');\">" +
                                        note.UserName +  "</p>" +
                                        note.Body +
                                        "</div>";
                        }
                    }

                    comments += "</div>";

                    string avatar_email = "";
                    if (File.Exists (GetAvatar (change_set.UserEmail, 48)))
                        avatar_email = change_set.UserEmail;

                    event_entry   += "</dl>";
                    event_entries += event_entry_html.Replace ("<!-- $event-entry-content -->", event_entry)
                        .Replace ("<!-- $event-user-name -->", change_set.UserName)
                        .Replace ("<!-- $event-avatar-url -->", "file://" + GetAvatar (avatar_email, 48))
                        .Replace ("<!-- $event-time -->", change_set.Timestamp.ToString ("H:mm"))
                        .Replace ("<!-- $event-folder -->", change_set.Folder)
                        .Replace ("<!-- $event-revision -->", change_set.Revision)
                        .Replace ("<!-- $event-folder-color -->", AssignColor (change_set.Folder))
                        .Replace ("<!-- $event-comments -->", comments);
                }

                string day_entry   = "";
                DateTime today     = DateTime.Now;
                DateTime yesterday = DateTime.Now.AddDays (-1);

                if (today.Day   == activity_day.DateTime.Day &&
                    today.Month == activity_day.DateTime.Month && 
                    today.Year  == activity_day.DateTime.Year) {

                    day_entry = day_entry_html.Replace ("<!-- $day-entry-header -->", "Today");

                } else if (yesterday.Day   == activity_day.DateTime.Day &&
                           yesterday.Month == activity_day.DateTime.Month &&
                           yesterday.Year  == activity_day.DateTime.Year) {

                    day_entry = day_entry_html.Replace ("<!-- $day-entry-header -->", "Yesterday");

                } else {
                    if (activity_day.DateTime.Year != DateTime.Now.Year) {
                        // TRANSLATORS: This is the date in the event logs
                        day_entry = day_entry_html.Replace ("<!-- $day-entry-header -->",
                            activity_day.DateTime.ToString (_("dddd, MMMM d, yyyy")));

                    } else {
                        // TRANSLATORS: This is the date in the event logs, without the year
                        day_entry = day_entry_html.Replace ("<!-- $day-entry-header -->",
                            activity_day.DateTime.ToString (_("dddd, MMMM d")));
                    }
                }

                event_log += day_entry.Replace ("<!-- $day-entry-content -->", event_entries);
            }

            string html =  event_log_html.Replace ("<!-- $event-log-content -->", event_log)
                .Replace ("<!-- $username -->", UserName)
                .Replace ("<!-- $user-avatar-url -->", "file://" + GetAvatar (UserEmail, 48));

            return html;
        }


        // Creates a .desktop entry in autostart folder to
        // start SparkleShare automatically at login
        public abstract void EnableSystemAutostart ();

        // Installs a launcher so the user can launch SparkleShare
        // from the Internet category if needed
        public abstract void InstallLauncher ();

        // Adds the SparkleShare folder to the user's
        // list of bookmarked places
        public abstract void AddToBookmarks ();

        // Creates the SparkleShare folder in the user's home folder
        public abstract bool CreateSparkleShareFolder ();

        // Opens the SparkleShare folder or an (optional) subfolder
        public abstract void OpenSparkleShareFolder (string subfolder);


        // Fires events for the current syncing state
        public void UpdateState ()
        {
            foreach (SparkleRepoBase repo in Repositories) {
                if (repo.Status == SyncStatus.SyncDown ||
                    repo.Status == SyncStatus.SyncUp   ||
                    repo.IsBuffering) {

                    if (OnSyncing != null)
                        OnSyncing ();

                    return;

                } else if (repo.HasUnsyncedChanges) {
                    if (OnError != null)
                        OnError ();

                    return;
                }
            }

            if (OnIdle != null)
                OnIdle ();

            FolderSize = GetFolderSize ();

            if (FolderSizeChanged != null)
                FolderSizeChanged (FolderSize);
        }


        // Adds a repository to the list of repositories
        private void AddRepository (string folder_path)
        {
            if (folder_path.Equals (SparklePaths.SparkleTmpPath))
                return;

            string folder_name = Path.GetFileName (folder_path);
            string backend = SparkleConfig.DefaultConfig.GetBackendForFolder (folder_name);

            if (backend == null)
                return;
            
            SparkleRepoBase repo = null;

            if (backend.Equals ("Hg"))
                repo = new SparkleRepoHg (folder_path, new SparkleBackendHg ());

            else if (backend.Equals ("Scp"))
                repo = new SparkleRepoScp (folder_path, new SparkleBackendScp ());

            else
               repo = new SparkleRepoGit (folder_path, SparkleBackend.DefaultBackend);

            repo.NewChangeSet += delegate (SparkleChangeSet change_set, string repository_path) {
                string message = FormatMessage (change_set);

                if (NotificationRaised != null)
                    NotificationRaised (change_set.UserName, change_set.UserEmail, message, repository_path);
            };

            repo.ConflictResolved += delegate {
                if (ConflictNotificationRaised != null)
                    ConflictNotificationRaised ();
            };

            repo.SyncStatusChanged += delegate (SyncStatus status) {
/*                if (status == SyncStatus.SyncUp) {
                    foreach (string path in repo.UnsyncedFilePaths)
                        Console.WriteLine (path);
                }
*/
                if (status == SyncStatus.Idle     ||
                    status == SyncStatus.SyncUp   ||
                    status == SyncStatus.SyncDown ||
                    status == SyncStatus.Error) {

                    UpdateState ();
                }
            };

            repo.ChangesDetected += delegate {
                UpdateState ();
            };

            Repositories.Add (repo);
        }


        // Removes a repository from the list of repositories and
        // updates the statusicon menu
        private void RemoveRepository (string folder_path)
        {
            string folder_name = Path.GetFileName (folder_path);

            for (int i = 0; i < Repositories.Count; i++) {
                SparkleRepoBase repo = Repositories [i];

                if (repo.Name.Equals (folder_name)) {
                    repo.Dispose ();
                    Repositories.Remove (repo);
                    repo = null;
                    break;
                }
            }
        }


        // Updates the list of repositories with all the
        // folders in the SparkleShare folder
        private void PopulateRepositories ()
        {
            Repositories = new List<SparkleRepoBase> ();

            foreach (string folder_name in SparkleConfig.DefaultConfig.Folders) {
                string folder_path = Path.Combine (SparklePaths.SparklePath, folder_name);

                if (Directory.Exists (folder_path))
                    AddRepository (folder_path);
                else
                    SparkleConfig.DefaultConfig.RemoveFolder (folder_name);
            }

            if (FolderListChanged != null)
                FolderListChanged ();
            
            FolderSize = GetFolderSize ();

            if (FolderSizeChanged != null)
                FolderSizeChanged (FolderSize);
        }


        public bool NotificationsEnabled {
            get {
                string notifications_enabled =
                    SparkleConfig.DefaultConfig.GetConfigOption ("notifications");

                if (String.IsNullOrEmpty (notifications_enabled)) {
                    SparkleConfig.DefaultConfig.SetConfigOption ("notifications", bool.TrueString);
                    return true;

                } else {
                    return notifications_enabled.Equals (bool.TrueString);
                }
            }
        } 


        public void ToggleNotifications () {
            bool notifications_enabled =
                SparkleConfig.DefaultConfig.GetConfigOption ("notifications")
                    .Equals (bool.TrueString);

            if (notifications_enabled)
                SparkleConfig.DefaultConfig.SetConfigOption ("notifications", bool.FalseString);
            else
                SparkleConfig.DefaultConfig.SetConfigOption ("notifications", bool.TrueString);
        }


        private string GetFolderSize ()
        {
            double folder_size = CalculateFolderSize (new DirectoryInfo (SparklePaths.SparklePath));
            return FormatFolderSize (folder_size);
        }


        private string FormatMessage (SparkleChangeSet change_set)
        {
            string file_name = "";
            string message   = "";

            if (change_set.Added.Count > 0) {
                file_name = change_set.Added [0];
                message = String.Format (_("added ‘{0}’"), file_name);
            }

            if (change_set.MovedFrom.Count > 0) {
                file_name = change_set.MovedFrom [0];
                message = String.Format (_("moved ‘{0}’"), file_name);
            }

            if (change_set.Edited.Count > 0) {
                file_name = change_set.Edited [0];
                message = String.Format (_("edited ‘{0}’"), file_name);
            }

            if (change_set.Deleted.Count > 0) {
                file_name = change_set.Deleted [0];
                message = String.Format (_("deleted ‘{0}’"), file_name);
            }

            int changes_count = (change_set.Added.Count +
                                 change_set.Edited.Count +
                                 change_set.Deleted.Count +
                                 change_set.MovedFrom.Count) - 1;

            if (changes_count > 0) {
                string msg = Catalog.GetPluralString ("and {0} more", "and {0} more", changes_count);
                message += " " + String.Format (msg, changes_count);

            } else if (changes_count < 0) {
                message += _("did something magical");
            }

            return message;
        }


        // Recursively gets a folder's size in bytes
        private double CalculateFolderSize (DirectoryInfo parent)
        {
            if (!Directory.Exists (parent.ToString ()))
                return 0;

            double size = 0;

            // Ignore the temporary 'rebase-apply' and '.tmp' directories. This prevents potential
            // crashes when files are being queried whilst the files have already been deleted.
            if (parent.Name.Equals ("rebase-apply") ||
                parent.Name.Equals (".tmp"))
                return 0;

            try {
                foreach (FileInfo file in parent.GetFiles()) {
                    if (!file.Exists)
                        return 0;

                    size += file.Length;
                }

                foreach (DirectoryInfo directory in parent.GetDirectories())
                    size += CalculateFolderSize (directory);

            } catch (Exception) {
                return 0;
            }

            return size;
        }


        // Format a file size nicely with small caps.
        // Example: 1048576 becomes "1 ᴍʙ"
        private string FormatFolderSize (double byte_count)
        {
            if (byte_count >= 1099511627776)
                return String.Format ("{0:##.##} ᴛʙ", Math.Round (byte_count / 1099511627776, 1));
            else if (byte_count >= 1073741824)
                return String.Format ("{0:##.##} ɢʙ", Math.Round (byte_count / 1073741824, 1));
            else if (byte_count >= 1048576)
                return String.Format ("{0:##.##} ᴍʙ", Math.Round (byte_count / 1048576, 1));
            else if (byte_count >= 1024)
                return String.Format ("{0:##.##} ᴋʙ", Math.Round (byte_count / 1024, 1));
            else
                return byte_count.ToString () + " bytes";
        }


        public void OpenSparkleShareFolder ()
        {
            OpenSparkleShareFolder ("");
        }

        
        // Adds the user's SparkleShare key to the ssh-agent,
        // so all activity is done with this key
        public void AddKey ()
        {
            string keys_path = SparklePaths.SparkleConfigPath;
            string key_file_name = "sparkleshare." + UserEmail + ".key";

            Process process = new Process ();
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute        = false;
            process.StartInfo.FileName               = "ssh-add";
            process.StartInfo.Arguments              = "\"" + Path.Combine (keys_path, key_file_name) + "\"";
            process.Start ();
            process.WaitForExit ();
        }


        public bool BackendIsPresent {
            get {
                return SparkleBackend.DefaultBackend.IsPresent;
            }
        }


        // Looks up the user's name from the global configuration
        public string UserName
        {
            get {
                return SparkleConfig.DefaultConfig.UserName;
            }

            set {
                SparkleConfig.DefaultConfig.UserName = value;
            }
        }


        // Looks up the user's email from the global configuration
        public string UserEmail
        {
            get {
                return SparkleConfig.DefaultConfig.UserEmail;
            }
                    
            set {
                SparkleConfig.DefaultConfig.UserEmail = value;
            }
        }
        

        // Generates and installs an RSA keypair to identify this system
        public void GenerateKeyPair ()
        {
            string keys_path     = SparklePaths.SparkleConfigPath;
            string key_file_name = "sparkleshare." + UserEmail + ".key";
            string key_file_path = Path.Combine (keys_path, key_file_name);

            if (File.Exists (key_file_path)) {
                SparkleHelpers.DebugInfo ("Config", "Key already exists ('" + key_file_name + "'), " +
                                          "leaving it untouched");
                return;
            }

            if (!Directory.Exists (keys_path))
                Directory.CreateDirectory (keys_path);

            if (!File.Exists (key_file_name)) {
                Process process = new Process () {
                    EnableRaisingEvents = true
                };
                
                process.StartInfo.WorkingDirectory = keys_path;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.FileName = "ssh-keygen";

                // -t is the crypto type
                // -P is the password (none)
                // -f is the file name to store the private key in
                process.StartInfo.Arguments = "-t rsa -P \"\" -f " + key_file_name;

                process.Exited += delegate {
                    SparkleHelpers.DebugInfo ("Config", "Created private key '" + key_file_name + "'");
                    SparkleHelpers.DebugInfo ("Config", "Created public key  '" + key_file_name + ".pub'");

                    // Create an easily accessible copy of the public
                    // key in the user's SparkleShare folder
                    File.Copy (key_file_path + ".pub",
                        Path.Combine (SparklePath, UserName + "'s key.txt"));
                };
                
                process.Start ();
                process.WaitForExit ();
            }
        }


        // Gets the avatar for a specific email address and size
        public void FetchAvatars (List<string> emails, int size)
        {
            List<string> old_avatars = new List<string> ();
            bool avatar_fetched      = false;
            string avatar_path       = SparkleHelpers.CombineMore (
                SparklePaths.SparkleLocalIconPath, size + "x" + size, "status");

            if (!Directory.Exists (avatar_path)) {
                Directory.CreateDirectory (avatar_path);
                SparkleHelpers.DebugInfo ("Config", "Created '" + avatar_path + "'");
            }

            foreach (string email in emails) {
                string avatar_file_path = Path.Combine (avatar_path, "avatar-" + email);

                if (File.Exists (avatar_file_path)) {
                    FileInfo avatar_info = new FileInfo (avatar_file_path);

                    // Delete avatars older than a month
                    if (avatar_info.CreationTime < DateTime.Now.AddMonths (-1)) {
                        avatar_info.Delete ();
                        old_avatars.Add (email);
                    }

                } else {
                  WebClient client = new WebClient ();
                  string url       =  "http://gravatar.com/avatar/" + GetMD5 (email) +
                                      ".jpg?s=" + size + "&d=404";

                  try {
                    // Fetch the avatar
                    byte [] buffer = client.DownloadData (url);

                    // Write the avatar data to a
                    // if not empty
                    if (buffer.Length > 255) {
                        avatar_fetched = true;
                        File.WriteAllBytes (avatar_file_path, buffer);
                        SparkleHelpers.DebugInfo ("Controller", "Fetched gravatar for " + email);
                    }

                  } catch (WebException) {
                        SparkleHelpers.DebugInfo ("Controller", "Failed fetching gravatar for " + email);
                  }
               }
            }

            // Fetch new versions of the avatars that we
            // deleted because they were too old
            if (old_avatars.Count > 0)
                FetchAvatars (old_avatars, size);

            if (AvatarFetched != null && avatar_fetched)
                AvatarFetched ();
        }


        public string GetAvatar (string email, int size)
        {
            string avatar_file_path = SparkleHelpers.CombineMore (
                SparklePaths.SparkleLocalIconPath, size + "x" + size, "status", "avatar-" + email);

            return avatar_file_path;
        }


        public void FetchFolder (string server, string remote_folder)
        {
            server = server.Trim ();
            remote_folder = remote_folder.Trim ();

            if (!Directory.Exists (SparklePaths.SparkleTmpPath))
                Directory.CreateDirectory (SparklePaths.SparkleTmpPath);

            // Strip the '.git' from the name
            string canonical_name = Path.GetFileNameWithoutExtension (remote_folder);
            string tmp_folder     = Path.Combine (SparklePaths.SparkleTmpPath, canonical_name);

            SparkleFetcherBase fetcher = null;
            string backend = null;

            if (remote_folder.EndsWith (".hg")) {
                remote_folder = remote_folder.Substring (0, (remote_folder.Length - 3));
                fetcher       = new SparkleFetcherHg (server, remote_folder, tmp_folder);
                backend       = "Hg";

            } else if (remote_folder.EndsWith (".scp")) {
                remote_folder = remote_folder.Substring (0, (remote_folder.Length - 4));
                fetcher = new SparkleFetcherScp (server, remote_folder, tmp_folder);
                backend = "Scp";

            } else {
                fetcher = new SparkleFetcherGit (server, remote_folder, tmp_folder);
                backend = "Git";
            }

            bool target_folder_exists = Directory.Exists (Path.Combine (SparklePaths.SparklePath, canonical_name));

            // Add a numbered suffix to the nameif a folder with the same name
            // already exists. Example: "Folder (2)"
            int i = 1;
            while (target_folder_exists) {
                i++;
                target_folder_exists = Directory.Exists (
                    Path.Combine (SparklePaths.SparklePath, canonical_name + " (" + i + ")"));
            }

            string target_folder_name = canonical_name;
            if (i > 1)
                target_folder_name += " (" + i + ")";

            fetcher.Finished += delegate {

                // Needed to do the moving
                SparkleHelpers.ClearAttributes (tmp_folder);
                string target_folder_path = Path.Combine (SparklePaths.SparklePath, target_folder_name);

                try {
                    Directory.Move (tmp_folder, target_folder_path);
                } catch (Exception e) {
                    SparkleHelpers.DebugInfo ("Controller", "Error moving folder: " + e.Message);
                }

                SparkleConfig.DefaultConfig.AddFolder (target_folder_name, fetcher.RemoteUrl, backend);
                AddRepository (target_folder_path);

                if (FolderFetched != null)
                    FolderFetched ();

                FolderSize = GetFolderSize ();

                if (FolderSizeChanged != null)
                    FolderSizeChanged (FolderSize);

                if (FolderListChanged != null)
                    FolderListChanged ();

                fetcher.Dispose ();

                if (Directory.Exists (SparklePaths.SparkleTmpPath))
                    Directory.Delete (SparklePaths.SparkleTmpPath, true);
            };


            fetcher.Failed += delegate {
                if (FolderFetchError != null)
                    FolderFetchError ();

                fetcher.Dispose ();

                if (Directory.Exists (SparklePaths.SparkleTmpPath))
                    Directory.Delete (SparklePaths.SparkleTmpPath, true);
            };


            fetcher.Start ();
        }


        // Creates an MD5 hash of input
        private string GetMD5 (string s)
        {
            MD5 md5 = new MD5CryptoServiceProvider ();
            Byte[] bytes = ASCIIEncoding.Default.GetBytes (s);
            Byte[] encoded_bytes = md5.ComputeHash (bytes);
            return BitConverter.ToString (encoded_bytes).ToLower ().Replace ("-", "");
        }


        // Checks whether there are any folders syncing and
        // quits if safe
        public void TryQuit ()
        {
            foreach (SparkleRepoBase repo in Repositories) {
                if (repo.Status == SyncStatus.SyncUp   ||
                    repo.Status == SyncStatus.SyncDown ||
                    repo.IsBuffering) {

                    if (OnQuitWhileSyncing != null)
                        OnQuitWhileSyncing ();
                    
                    return;
                }
            }
            
            Quit ();
        }


        public void Quit ()
        {
            foreach (SparkleRepoBase repo in Repositories)
                repo.Dispose ();

            Environment.Exit (0);
        }


        // Checks to see if an email address is valid
        public bool IsValidEmail (string email)
        {
            Regex regex = new Regex (@"^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,4}$", RegexOptions.IgnoreCase);
            return regex.IsMatch (email);
        }




        public void AddNoteToFolder (string folder_name, string revision, string note)
        {
            foreach (SparkleRepoBase repo in Repositories) {
                if (repo.Name.Equals (folder_name))
                    repo.AddNote (revision, note);
            }
        }




        private string [] tango_palette = new string [] {"#eaab00", "#e37222",
            "#3892ab", "#33c2cb", "#19b271", "#9eab05", "#8599a8", "#9ca696",
            "#b88454", "#cc0033", "#8f6678", "#8c6cd0", "#796cbf", "#4060af",
            "#aa9c8f", "#818a8f"};

        private string AssignColor (string s)
        {
            string hash    = GetMD5 (s).Substring (0, 8);
            string numbers = Regex.Replace (hash, "[a-z]", "");
            int number     = 3 + int.Parse (numbers);
            return this.tango_palette [number % this.tango_palette.Length];
        }
    }


    public class ChangeSet : SparkleChangeSet { }
    
    
    // All change sets that happened on a day
    public class ActivityDay : List <SparkleChangeSet>
    {
        public DateTime DateTime;

        public ActivityDay (DateTime date_time)
        {
            DateTime = date_time;
            DateTime = new DateTime (DateTime.Year, DateTime.Month, DateTime.Day);
        }
    }
}



//using System;
//using System.Collections;
//using System.Globalization;
//using System.Text;

namespace Procurios.Public
{
    /// <summary>
    /// This class encodes and decodes JSON strings.
    /// Spec. details, see http://www.json.org/
    /// 
    /// JSON uses Arrays and Objects. These correspond here to the datatypes ArrayList and Hashtable.
    /// All numbers are parsed to doubles.
    /// </summary>
    public class JSON
    {
        public const int TOKEN_NONE = 0; 
        public const int TOKEN_CURLY_OPEN = 1;
        public const int TOKEN_CURLY_CLOSE = 2;
        public const int TOKEN_SQUARED_OPEN = 3;
        public const int TOKEN_SQUARED_CLOSE = 4;
        public const int TOKEN_COLON = 5;
        public const int TOKEN_COMMA = 6;
        public const int TOKEN_STRING = 7;
        public const int TOKEN_NUMBER = 8;
        public const int TOKEN_TRUE = 9;
        public const int TOKEN_FALSE = 10;
        public const int TOKEN_NULL = 11;

        private const int BUILDER_CAPACITY = 2000;

        /// <summary>
        /// Parses the string json into a value
        /// </summary>
        /// <param name="json">A JSON string.</param>
        /// <returns>An ArrayList, a Hashtable, a double, a string, null, true, or false</returns>
        public static object JsonDecode(string json)
        {
            bool success = true;
            
            return JsonDecode(json, ref success);
        }

        /// <summary>
        /// Parses the string json into a value; and fills 'success' with the successfullness of the parse.
        /// </summary>
        /// <param name="json">A JSON string.</param>
        /// <param name="success">Successful parse?</param>
        /// <returns>An ArrayList, a Hashtable, a double, a string, null, true, or false</returns>
        public static object JsonDecode(string json, ref bool success)
        {
            success = true;
            if (json != null) {
                char[] charArray = json.ToCharArray();
                int index = 0;
                object value = ParseValue(charArray, ref index, ref success);
                return value;
            } else {
                return null;
            }
        }
    
        /// <summary>
        /// Converts a Hashtable / ArrayList object into a JSON string
        /// </summary>
        /// <param name="json">A Hashtable / ArrayList</param>
        /// <returns>A JSON encoded string, or null if object 'json' is not serializable</returns>
        public static string JsonEncode(object json)
        {
            StringBuilder builder = new StringBuilder(BUILDER_CAPACITY);
            bool success = SerializeValue(json, builder);
            return (success ? builder.ToString() : null);
        }

        protected static Hashtable ParseObject(char[] json, ref int index, ref bool success)
        {
            Hashtable table = new Hashtable();
            int token;

            // {
            NextToken(json, ref index);

            bool done = false;
            while (!done) {
                token = LookAhead(json, index);
                if (token == JSON.TOKEN_NONE) {
                    success = false;
                    return null;
                } else if (token == JSON.TOKEN_COMMA) {
                    NextToken(json, ref index);
                } else if (token == JSON.TOKEN_CURLY_CLOSE) {
                    NextToken(json, ref index);
                    return table;
                } else {

                    // name
                    string name = ParseString(json, ref index, ref success);
                    if (!success) {
                        success = false;
                        return null;
                    }

                    // :
                    token = NextToken(json, ref index);
                    if (token != JSON.TOKEN_COLON) {
                        success = false;
                        return null;
                    }

                    // value
                    object value = ParseValue(json, ref index, ref success);
                    if (!success) {
                        success = false;
                        return null;
                    }

                    table[name] = value;
                }
            }

            return table;
        }

        protected static ArrayList ParseArray(char[] json, ref int index, ref bool success)
        {
            ArrayList array = new ArrayList();

            // [
            NextToken(json, ref index);

            bool done = false;
            while (!done) {
                int token = LookAhead(json, index);
                if (token == JSON.TOKEN_NONE) {
                    success = false;
                    return null;
                } else if (token == JSON.TOKEN_COMMA) {
                    NextToken(json, ref index);
                } else if (token == JSON.TOKEN_SQUARED_CLOSE) {
                    NextToken(json, ref index);
                    break;
                } else {
                    object value = ParseValue(json, ref index, ref success);
                    if (!success) {
                        return null;
                    }

                    array.Add(value);
                }
            }

            return array;
        }

        protected static object ParseValue(char[] json, ref int index, ref bool success)
        {
            switch (LookAhead(json, index)) {
                case JSON.TOKEN_STRING:
                    return ParseString(json, ref index, ref success);
                case JSON.TOKEN_NUMBER:
                    return ParseNumber(json, ref index, ref success);
                case JSON.TOKEN_CURLY_OPEN:
                    return ParseObject(json, ref index, ref success);
                case JSON.TOKEN_SQUARED_OPEN:
                    return ParseArray(json, ref index, ref success);
                case JSON.TOKEN_TRUE:
                    NextToken(json, ref index);
                    return true;
                case JSON.TOKEN_FALSE:
                    NextToken(json, ref index);
                    return false;
                case JSON.TOKEN_NULL:
                    NextToken(json, ref index);
                    return null;
                case JSON.TOKEN_NONE:
                    break;
            }

            success = false;
            return null;
        }

        protected static string ParseString(char[] json, ref int index, ref bool success)
        {
            StringBuilder s = new StringBuilder(BUILDER_CAPACITY);
            char c;

            EatWhitespace(json, ref index);
            
            // "
            c = json[index++];

            bool complete = false;
            while (!complete) {

                if (index == json.Length) {
                    break;
                }

                c = json[index++];
                if (c == '"') {
                    complete = true;
                    break;
                } else if (c == '\\') {

                    if (index == json.Length) {
                        break;
                    }
                    c = json[index++];
                    if (c == '"') {
                        s.Append('"');
                    } else if (c == '\\') {
                        s.Append('\\');
                    } else if (c == '/') {
                        s.Append('/');
                    } else if (c == 'b') {
                        s.Append('\b');
                    } else if (c == 'f') {
                        s.Append('\f');
                    } else if (c == 'n') {
                        s.Append('\n');
                    } else if (c == 'r') {
                        s.Append('\r');
                    } else if (c == 't') {
                        s.Append('\t');
                    } else if (c == 'u') {
                        int remainingLength = json.Length - index;
                        if (remainingLength >= 4) {
                            // parse the 32 bit hex into an integer codepoint
                            uint codePoint;
                            if (!(success = UInt32.TryParse(new string(json, index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out codePoint))) {
                                return "";
                            }
                            // convert the integer codepoint to a unicode char and add to string
                            s.Append(Char.ConvertFromUtf32((int)codePoint));
                            // skip 4 chars
                            index += 4;
                        } else {
                            break;
                        }
                    }

                } else {
                    s.Append(c);
                }

            }

            if (!complete) {
                success = false;
                return null;
            }

            return s.ToString();
        }

        protected static double ParseNumber(char[] json, ref int index, ref bool success)
        {
            EatWhitespace(json, ref index);

            int lastIndex = GetLastIndexOfNumber(json, index);
            int charLength = (lastIndex - index) + 1;

            double number;
            success = Double.TryParse(new string(json, index, charLength), NumberStyles.Any, CultureInfo.InvariantCulture, out number);

            index = lastIndex + 1;
            return number;
        }

        protected static int GetLastIndexOfNumber(char[] json, int index)
        {
            int lastIndex;

            for (lastIndex = index; lastIndex < json.Length; lastIndex++) {
                if ("0123456789+-.eE".IndexOf(json[lastIndex]) == -1) {
                    break;
                }
            }
            return lastIndex - 1;
        }

        protected static void EatWhitespace(char[] json, ref int index)
        {
            for (; index < json.Length; index++) {
                if (" \t\n\r".IndexOf(json[index]) == -1) {
                    break;
                }
            }
        }

        protected static int LookAhead(char[] json, int index)
        {
            int saveIndex = index;
            return NextToken(json, ref saveIndex);
        }

        protected static int NextToken(char[] json, ref int index)
        {
            EatWhitespace(json, ref index);

            if (index == json.Length) {
                return JSON.TOKEN_NONE;
            }
            
            char c = json[index];
            index++;
            switch (c) {
                case '{':
                    return JSON.TOKEN_CURLY_OPEN;
                case '}':
                    return JSON.TOKEN_CURLY_CLOSE;
                case '[':
                    return JSON.TOKEN_SQUARED_OPEN;
                case ']':
                    return JSON.TOKEN_SQUARED_CLOSE;
                case ',':
                    return JSON.TOKEN_COMMA;
                case '"':
                    return JSON.TOKEN_STRING;
                case '0': case '1': case '2': case '3': case '4': 
                case '5': case '6': case '7': case '8': case '9':
                case '-': 
                    return JSON.TOKEN_NUMBER;
                case ':':
                    return JSON.TOKEN_COLON;
            }
            index--;

            int remainingLength = json.Length - index;

            // false
            if (remainingLength >= 5) {
                if (json[index] == 'f' &&
                    json[index + 1] == 'a' &&
                    json[index + 2] == 'l' &&
                    json[index + 3] == 's' &&
                    json[index + 4] == 'e') {
                    index += 5;
                    return JSON.TOKEN_FALSE;
                }
            }

            // true
            if (remainingLength >= 4) {
                if (json[index] == 't' &&
                    json[index + 1] == 'r' &&
                    json[index + 2] == 'u' &&
                    json[index + 3] == 'e') {
                    index += 4;
                    return JSON.TOKEN_TRUE;
                }
            }

            // null
            if (remainingLength >= 4) {
                if (json[index] == 'n' &&
                    json[index + 1] == 'u' &&
                    json[index + 2] == 'l' &&
                    json[index + 3] == 'l') {
                    index += 4;
                    return JSON.TOKEN_NULL;
                }
            }

            return JSON.TOKEN_NONE;
        }

        protected static bool SerializeValue(object value, StringBuilder builder)
        {
            bool success = true;

            if (value is string) {
                success = SerializeString((string)value, builder);
            } else if (value is Hashtable) {
                success = SerializeObject((Hashtable)value, builder);
            } else if (value is ArrayList) {
                success = SerializeArray((ArrayList)value, builder);
            } else if (IsNumeric(value)) {
                success = SerializeNumber(Convert.ToDouble(value), builder);
            } else if ((value is Boolean) && ((Boolean)value == true)) {
                builder.Append("true");
            } else if ((value is Boolean) && ((Boolean)value == false)) {
                builder.Append("false");
            } else if (value == null) {
                builder.Append("null");
            } else {
                success = false;
            }
            return success;
        }
        
        protected static bool SerializeObject(Hashtable anObject, StringBuilder builder)
        {
            builder.Append("{");

            IDictionaryEnumerator e = anObject.GetEnumerator();
            bool first = true;
            while (e.MoveNext()) {
                string key = e.Key.ToString();
                object value = e.Value;

                if (!first) {
                    builder.Append(", ");
                }

                SerializeString(key, builder);
                builder.Append(":");
                if (!SerializeValue(value, builder)) {
                    return false;
                }

                first = false;
            }

            builder.Append("}");
            return true;
        }

        protected static bool SerializeArray(ArrayList anArray, StringBuilder builder)
        {
            builder.Append("[");

            bool first = true;
            for (int i = 0; i < anArray.Count; i++) {
                object value = anArray[i];

                if (!first) {
                    builder.Append(", ");
                }

                if (!SerializeValue(value, builder)) {
                    return false;
                }

                first = false;
            }

            builder.Append("]");
            return true;
        }

        protected static bool SerializeString(string aString, StringBuilder builder)
        {
            builder.Append("\"");

            char[] charArray = aString.ToCharArray();
            for (int i = 0; i < charArray.Length; i++) {
                char c = charArray[i];
                if (c == '"') {
                    builder.Append("\\\"");
                } else if (c == '\\') {
                    builder.Append("\\\\");
                } else if (c == '\b') {
                    builder.Append("\\b");
                } else if (c == '\f') {
                    builder.Append("\\f");
                } else if (c == '\n') {
                    builder.Append("\\n");
                } else if (c == '\r') {
                    builder.Append("\\r");
                } else if (c == '\t') {
                    builder.Append("\\t");
                } else {
                    int codepoint = Convert.ToInt32(c);
                    if ((codepoint >= 32) && (codepoint <= 126)) {
                        builder.Append(c);
                    } else {
                        builder.Append("\\u" + Convert.ToString(codepoint, 16).PadLeft(4, '0'));
                    }
                }
            }

            builder.Append("\"");
            return true;
        }

        protected static bool SerializeNumber(double number, StringBuilder builder)
        {
            builder.Append(Convert.ToString(number, CultureInfo.InvariantCulture));
            return true;
        }

        /// <summary>
        /// Determines if a given object is numeric in any way
        /// (can be integer, double, null, etc). 
        /// 
        /// Thanks to mtighe for pointing out Double.TryParse to me.
        /// </summary>
        protected static bool IsNumeric(object o)
        {
            double result;

            return (o == null) ? false : Double.TryParse(o.ToString(), out result);
        }
    }
}
