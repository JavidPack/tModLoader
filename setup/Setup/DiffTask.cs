﻿using DiffPatch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Terraria.ModLoader.Setup
{
	public class DiffTask : Task
	{
		private static string[] extensions = { ".cs", ".csproj", ".ico", ".resx", ".png", "App.config", ".json" };
		private static bool IsDiffable(string relPath) => extensions.Any(relPath.EndsWith);

		public static readonly string RemovedFileList = "removed_files.list";
		public static readonly Regex HunkOffsetRegex = new Regex(@"@@ -(\d+),(\d+) \+([_\d]+),(\d+) @@", RegexOptions.Compiled);
		
		public readonly string baseDir;
		public readonly string patchedDir;
		public readonly string patchDir;
		public readonly ProgramSetting<DateTime> cutoff;

		public DiffTask(ITaskInterface taskInterface, string baseDir, string srcDir, string patchDir, 
			ProgramSetting<DateTime> cutoff) : base(taskInterface)
		{
			this.baseDir = baseDir;
			this.patchedDir = srcDir;
			this.patchDir = patchDir;
			this.cutoff = cutoff;
		}

		public override void Run()
		{
			var items = new List<WorkItem>();
			
			foreach (var (file, relPath) in PatchTask.EnumerateSrcFiles(patchedDir))
			{
				if (File.GetLastWriteTime(file) < cutoff.Get())
					continue;

				if (!File.Exists(Path.Combine(baseDir, relPath)))
					items.Add(new WorkItem("Copying: " + relPath, () => Copy(file, Path.Combine(patchDir, relPath))));
				else if (IsDiffable(relPath))
					items.Add(new WorkItem("Diffing: " + relPath, () => Diff(relPath)));
			}

			ExecuteParallel(items);

			taskInterface.SetStatus("Deleting Unnessesary Patches");
			foreach (var (file, relPath) in EnumerateFiles(patchDir)) {
				var targetPath = relPath.EndsWith(".patch") ? relPath.Substring(0, relPath.Length - 6) : relPath;
				if (!File.Exists(Path.Combine(patchedDir, targetPath)))
					DeleteFile(file);
			}

			DeleteEmptyDirs(patchDir);

			taskInterface.SetStatus("Noting Removed Files");
			var removedFiles = PatchTask.EnumerateSrcFiles(baseDir)
				.Where(f => !File.Exists(Path.Combine(patchedDir, f.relPath)))
				.Select(f => f.relPath)
				.ToArray();

			var removedFileList = Path.Combine(patchDir, RemovedFileList);
			if (removedFiles.Length > 0)
				File.WriteAllLines(removedFileList, removedFiles);
			else
				DeleteFile(removedFileList);

			cutoff.Set(DateTime.Now);
		}

		private void Diff(string relPath)
		{
			var patchFile = Differ.DiffFiles(new LineMatchedDiffer(), Path.Combine(baseDir, relPath), Path.Combine(patchedDir, relPath));
			var patchPath = Path.Combine(patchDir, relPath + ".patch");
			if (!patchFile.IsEmpty) {
				CreateParentDirectory(patchPath);
				File.WriteAllText(patchPath, patchFile.ToString(true));
			}
			else
				DeleteFile(patchPath);
		}

		private static string StripDestHunkOffsets(string patchText) {
			var lines = patchText.Split(new [] { Environment.NewLine }, StringSplitOptions.None);
			for (int i = 0; i < lines.Length; i++)
				if (lines[i].StartsWith("@@"))
					lines[i] = HunkOffsetRegex.Replace(lines[i], "@@ -$1,$2 +_,$4 @@");

			return string.Join(Environment.NewLine, lines);
		}

		private string CallDiff(string baseFile, string srcFile, string baseName, string srcName)
		{
			var output = new StringBuilder();
			Program.RunCmd(Program.toolsDir, Path.Combine(Program.toolsDir, "py.exe"),
				$"diff.py {baseFile} {srcFile} {baseName} {srcName}",
				s => output.Append(s));

			return output.ToString();
		}

	}
}