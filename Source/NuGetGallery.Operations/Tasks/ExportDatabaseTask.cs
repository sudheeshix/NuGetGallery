﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using NuGetGallery.Operations.Common;
using NuGetGallery.Operations.SqlDac;

namespace NuGetGallery.Operations
{
    [Command("exportdatabase", "Exports a copy of the database to blob storage", AltName = "xdb", MinArgs = 0, MaxArgs = 0)]
    public class ExportDatabaseTask : DatabaseTask
    {
        private IList<string> _unsanitizedUsers = new List<string>();

        [Option("Azure Storage Account in which the exported database should be placed", AltName = "s")]
        public CloudStorageAccount DestinationStorage { get; set; }

        [Option("Blob container in which the backup should be placed", AltName = "c")]
        public string DestinationContainer { get; set; }

        [Option("The name of the database to export (if not specified, the one in the connection string will be used)", AltName = "dbname")]
        public string DatabaseName { get; set; }

        [Option("URL of the SQL DAC endpoint to talk to", AltName = "dac")]
        public Uri SqlDacEndpoint { get; set; }

        public override void ValidateArguments()
        {
            base.ValidateArguments();

            if (CurrentEnvironment != null)
            {
                if (DestinationStorage == null)
                {
                    DestinationStorage = CurrentEnvironment.BackupStorage;
                }
                if (SqlDacEndpoint == null)
                {
                    SqlDacEndpoint = CurrentEnvironment.SqlDacEndpoint;
                }
            }

            ArgCheck.RequiredOrConfig(DestinationStorage, "DestinationStorage");
            ArgCheck.RequiredOrConfig(SqlDacEndpoint, "SqlDacEndpoint");
            ArgCheck.Required(DestinationContainer, "DestinationContainer");
        }

        public override void ExecuteCommand()
        {
            if (!String.IsNullOrEmpty(DatabaseName))
            {
                ConnectionString.InitialCatalog = DatabaseName;
            }
            Log.Info("Exporting {0} on {1} to {2}", ConnectionString.InitialCatalog, Util.GetDatabaseServerName(ConnectionString), DestinationStorage.Credentials.AccountName);

            string serverName = ConnectionString.DataSource;
            if (serverName.StartsWith("tcp:"))
            {
                serverName = serverName.Substring(4);
            }

            WASDImportExport.ImportExportHelper helper = new WASDImportExport.ImportExportHelper(Log)
            {
                EndPointUri = SqlDacEndpoint.AbsoluteUri,
                DatabaseName = ConnectionString.InitialCatalog,
                ServerName = serverName,
                UserName = ConnectionString.UserID,
                Password = ConnectionString.Password,
                StorageKey = Convert.ToBase64String(DestinationStorage.Credentials.ExportKey())
            };
            
            // Prep the blob
            var client = DestinationStorage.CreateCloudBlobClient();
            var container = client.GetContainerReference(DestinationContainer);
            container.CreateIfNotExists();
            var blob = container.GetBlockBlobReference(ConnectionString.InitialCatalog + ".bacpac");
            Log.Info("Starting export to {0}", blob.Uri.AbsoluteUri);

            // Export!
            string blobUrl = helper.DoExport(blob.Uri.AbsoluteUri, WhatIf);

            Log.Info("*** EXPORT COMPLETE ***");
            if (!String.IsNullOrEmpty(blobUrl))
            {
                Log.Info("Output: {0}", blobUrl);
            }
        }
    }
}
