﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Runtime;

namespace Xamarin.Essentials
{
    public static partial class FilePicker
    {
#pragma warning disable SA1401 // Fields should be private
        internal static TaskCompletionSource<PickResult> CompletionSource;
#pragma warning restore SA1401 // Fields should be private

        static async Task<PickResult> PlatformPickFileAsync(PickOptions options)
        {
            // We only need the permission when accessing the file, but it's more natural
            // to ask the user first, then show the picker.
            await Permissions.RequireAsync(PermissionType.ReadExternalStorage);

            var tcs = new TaskCompletionSource<PickResult>();

            var previousCompletionSource = Interlocked.Exchange(ref CompletionSource, tcs);
            previousCompletionSource?.SetCanceled();

            var parentActivity = Platform.GetCurrentActivity(true);

            var intent = new Intent(parentActivity, typeof(FilePickerActivity));

            var allowedTypes = options?.FileTypes?.Value;
            if (allowedTypes != null)
                intent.PutExtra(FilePickerActivity.ExtraAllowedTypes, allowedTypes.ToArray());

            intent.PutExtra(FilePickerActivity.ExtraPickerTitle, options.PickerTitle ?? "Select file");

            parentActivity.StartActivity(intent);

            return await tcs.Task;
        }
    }

    public partial class FilePickerFileType
    {
        public static FilePickerFileType PlatformImageFileType() =>
            new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "image/png", "image/jpeg" } }
            });

        public static FilePickerFileType PlatformPngFileType() =>
            new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "image/png" } }
            });
    }

    [Activity(ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation | global::Android.Content.PM.ConfigChanges.ScreenSize)]
    class FilePickerActivity : Activity
    {
        public const string ExtraAllowedTypes = "EXTRA_ALLOWED_TYPES";

        public const string ExtraPickerTitle = "EXTRA_PICKER_TITLE";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var intent = new Intent(Intent.ActionGetContent);

            intent.SetType("*/*");

            var allowedTypes = Intent.GetStringArrayExtra(ExtraAllowedTypes);

            if (allowedTypes?.Length > 0)
            {
                intent.PutExtra(Intent.ExtraMimeTypes, allowedTypes);
            }

            intent.AddCategory(Intent.CategoryOpenable);

            var pickerTitle = Intent.GetStringExtra(ExtraPickerTitle);

            StartActivityForResult(Intent.CreateChooser(intent, pickerTitle), 0);
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            var tcs = Interlocked.Exchange(ref FilePicker.CompletionSource, null);

            if (resultCode == Result.Canceled ||
                data?.Data == null)
            {
                tcs.SetResult(null);
            }
            else
            {
                try
                {
                    var contentUri = data.Data;

                    var filePath = GetFilePath(contentUri);
                    var fileName = GetFileName(contentUri);

                    var result = new PickResult(filePath, fileName);

                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }

            Finish();
        }

        string GetFilePath(global::Android.Net.Uri contentUri)
        {
            if (contentUri.Scheme == "file")
            {
                return contentUri.Path;
            }

            // ask the content provider for the data column, which may contain the actual file path
            var path = QueryContentResolverColumn(contentUri, MediaStore.Files.FileColumns.Data);

            if (!string.IsNullOrEmpty(path) &&
                Path.IsPathRooted(path))
            {
                return path;
            }

            // fallback: use content URI
            return contentUri.ToString();
        }

        string GetFileName(global::Android.Net.Uri contentUri)
        {
            // resolve file name by querying content provider for display name
            var filename = QueryContentResolverColumn(contentUri, MediaStore.MediaColumns.DisplayName);

            if (!string.IsNullOrWhiteSpace(filename))
            {
                return filename;
            }
            else
            {
                return Path.GetFileName(WebUtility.UrlDecode(contentUri.ToString()));
            }
        }

        string QueryContentResolverColumn(global::Android.Net.Uri contentUri, string columnName)
        {
            string text = null;

            string[] projection = { columnName };
            var cursor = ContentResolver.Query(contentUri, projection, null, null, null);
            if (cursor != null)
            {
                try
                {
                    if (cursor.MoveToFirst())
                    {
                        var columnIndex = cursor.GetColumnIndex(columnName);
                        if (columnIndex != -1)
                        {
                            text = cursor.GetString(columnIndex);
                        }
                    }
                }
                finally
                {
                    cursor?.Close();
                }
            }

            return text;
        }
    }

    public partial class PickResult
    {
        Stream PlatformGetStream()
        {
            var contentUri = global::Android.Net.Uri.Parse(FileUri);
            if (contentUri.Scheme == "content")
                return Application.Context.ContentResolver.OpenInputStream(contentUri);
            else
                return File.OpenRead(contentUri.Path);
        }
    }
}
