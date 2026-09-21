// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

internal static class FileHasher
{
	private static readonly Lock ConsoleLock = new();

	internal static Dictionary<AbsoluteFilePath, string> HashFiles(IReadOnlyList<AbsoluteFilePath> filePaths)
	{
		ConcurrentDictionary<AbsoluteFilePath, string> results = new();

		Parallel.ForEach(filePaths, filePath =>
		{
			try
			{
				string hash = ComputeHash(filePath);
				results[filePath] = hash;

				lock (ConsoleLock)
				{
					Console.WriteLine($"  Hashed: {filePath.FileName} -> {hash[..12]}...");
				}
			}
			catch (IOException ex)
			{
				ReportSkipped(filePath, ex);
			}
			catch (UnauthorizedAccessException ex)
			{
				// Not an IOException, despite reading as one: a file the process is denied access to
				// would otherwise escape this delegate, and Parallel.ForEach would surface it as an
				// AggregateException that discards every hash the other threads had produced. This
				// matches Deduplicator.StillMatchesGroup, which catches both for the same read.
				ReportSkipped(filePath, ex);
			}
		});

		return new Dictionary<AbsoluteFilePath, string>(results);
	}

	private static void ReportSkipped(AbsoluteFilePath filePath, Exception ex)
	{
		lock (ConsoleLock)
		{
			Console.WriteLine($"  Error hashing {filePath.FileName}: {ex.Message}");
		}
	}

	internal static string ComputeHash(AbsoluteFilePath filePath)
	{
		using FileStream stream = File.OpenRead(filePath.WeakString);
		byte[] hashBytes = SHA256.HashData(stream);
		return Convert.ToHexStringLower(hashBytes);
	}
}
