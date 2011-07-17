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
using System.IO;
using System.Collections.Generic;

namespace SparkleLib {

    public class SparkleChangeSet {

        public string UserName;
        public string UserEmail;

        public string Folder;
        public string Revision;
        public DateTime Timestamp;
        public bool IsMerge       = false;

        public List<string> Added     = new List<string> ();
        public List<string> Deleted   = new List<string> ();
        public List<string> Edited    = new List<string> ();
        public List<string> MovedFrom = new List<string> ();
        public List<string> MovedTo   = new List<string> ();

        public List<SparkleNote> Notes = new List<SparkleNote> ();

        public string RelativeTimestamp {
            get {
                TimeSpan time_span = DateTime.Now - Timestamp;

                if (time_span <= TimeSpan.FromSeconds (60))
                    return "just now";

                if (time_span <= TimeSpan.FromMinutes (60))
                    return time_span.Minutes > 1
                        ? time_span.Minutes + " minutes ago"
                        : "a minute ago";

                if (time_span <= TimeSpan.FromHours (24))
                    return time_span.Hours > 1
                        ? time_span.Hours + " hours ago"
                        : "an hour ago";

                 if (time_span <= TimeSpan.FromDays (30))
                    return time_span.Days > 1
                        ? time_span.Days + " days ago"
                        : "a day ago";

                if (time_span <= TimeSpan.FromDays (365))
                    return time_span.Days > 30
                    ? (time_span.Days / 30) + " months ago"
                    : "a month ago";

                return time_span.Days > 365
                    ? (time_span.Days / 365) + " years ago"
                    : "a year ago";
           }
       }


       public string ToJSON ()
       {
            string n = Environment.NewLine;

            string json =
            "\"changeSet\": [" + n +
            "    {" + n +
            "        \"userName\": \"" + UserName + "\"," + n +
            "        \"userEmail\": \"" + UserEmail + "\"," + n +
            "        \"timestamp\": " + (int) (Timestamp - new DateTime (1970, 1, 1)).TotalSeconds + "," + n +
            "        \"path\": \"" + SparklePaths.SparklePath + "\"," + n +
            "        \"folder\": \"" + Folder + "\"," + n +
            "        \"revision\": \"" + Revision + "\"," + n +
            "        \"changes\": [" + n +
            "            {" + n +
            "                \"added\": [";

            foreach (string added in Added) {
                json += n +
            "                    {" + n +
            "                        \"path\": \"" + Path.GetPathRoot (added) + "\"," + n +
            "                        \"name\": \"" + Path.GetFileName (added) + "\"" + n +
            "                    },";
            }

            json = json.TrimEnd (",".ToCharArray ()) + n;
            json +=
            "                ]," + n +
            "                \"edited\": [";

            foreach (string edited in Edited) {
                json += n +
            "                    {" + n +
            "                        \"path\": \"" + Path.GetPathRoot (edited) + "\"," + n +
            "                        \"name\": \"" + Path.GetFileName (edited) +  "\"" + n +
            "                    },";
            }

            json = json.TrimEnd (",".ToCharArray ()) + n;
            json +=
            "                ]," + n +
            "                \"deleted\": [";

            foreach (string deleted in Deleted) {
                json += n +
            "                    {" + n +
            "                        \"path\": \"" + Path.GetPathRoot (deleted) + "\"," + n +
            "                        \"name\": \"" + Path.GetFileName (deleted) + "\"" + n +
            "                    },";
            }

            json = json.TrimEnd (",".ToCharArray ()) + n;
            json +=
            "                ]," + n +
            "                \"movedFrom\": [";

            foreach (string moved_from in MovedFrom) {
                json += n +
            "                    {" + n +
            "                        \"path\": \"" + Path.GetPathRoot (moved_from) + "\"," + n +
            "                        \"name\": \"" + Path.GetFileName (moved_from) + "\"" + n +
            "                    },";
            }

            json = json.TrimEnd (",".ToCharArray ()) + n;
            json +=
            "                ]," + n +
            "                \"movedTo\": [";

            foreach (string moved_to in MovedTo) {
                json += n +
            "                    {" + n +
            "                        \"path\": \"" + Path.GetPathRoot (moved_to) + "\"," + n +
            "                        \"name\": \"" + Path.GetFileName (moved_to) + "\"" + n +
            "                    },";
            }

            json = json.TrimEnd (",".ToCharArray ()) + n;
            json +=
            "                ]" + n +
            "            }" + n +
            "        ]," + n +
            "        \"notes\": [";

            foreach (SparkleNote note in Notes) {
                json += n +
            "            {" + n +
            "                \"userName\": \"" + note.UserName + "\"," + n +
            "                \"userEmail\": \"" + note.UserEmail + "\"," + n +
            "                \"timestamp\": " + (int) (note.Timestamp - new DateTime (1970, 1, 1)).TotalSeconds + "," + n +
            "                \"body\": \"" + note.Body + "\"" + n +
            "            },";
            }

            json = json.TrimEnd (",".ToCharArray ()) + n;
            json +=
            "        ]" + n +
            "    }" + n +
            "]" + n;

            return json;
        }
   }


    public class SparkleNote {

        public string UserName;
        public string UserEmail;

        public DateTime Timestamp;
        public string Body;
    }
}
