// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

internal static class FileHasher
{
	private static readonly Lock ConsoleLock = new();

	/// <summary>
	/// Hashes only the files that could have a duplicate, which are those whose size some other file
	/// shares.
	/// </summary>
	/// <remarks>
	/// Two files of different lengths can never match, so hashing a file whose size is unique in the
	/// tree reads it end to end for nothing. In a media library most large files have a size of their
	/// own, and the cold disks, USB drives and network shares this tool is pointed at make that wasted
	/// read the bulk of a run. Zero-byte files share a size like any other, so they still group. A file
	/// that cannot be sized is hashed anyway, so it is reported as unreadable exactly as before.
	/// </remarks>
	/// <param name="filePaths">The scanned files.</param>
	/// <param name="uniqueSize">The files left unhashed because no other file has their size.</param>
	/// <returns>The hash of every file that might have a duplicate and could be read.</returns>
	internal static Dictionary<AbsoluteFilePath, string> HashPossibleDuplicates(IReadOnlyList<AbsoluteFilePath> filePaths, out IReadOnlyList<AbsoluteFilePath> uniqueSize)
	{
		IGrouping<long?, AbsoluteFilePath>[] bySize = [.. filePaths.GroupBy(file => Deduplicator.TryGetSize(file, out long size) ? size : (long?)null)];

		uniqueSize = [.. bySize.Where(g => g.Key.HasValue && g.Count() == 1).SelectMany(g => g)];
		return HashFiles([.. bySize.Where(g => !g.Key.HasValue || g.Count() > 1).SelectMany(g => g)]);
	}

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
			// The full path, not the file name: index.js or IMG_0001.jpg can repeat dozens of times in
			// one tree, and this line is the only place the failing copy is ever named.
			Console.WriteLine($"  Error hashing {filePath}: {ex.Message}");
		}
	}

	internal static string ComputeHash(AbsoluteFilePath filePath)
	{
		using FileStream stream = File.OpenRead(filePath.WeakString);
		byte[] hashBytes = SHA256.HashData(stream);
		return Convert.ToHexStringLower(hashBytes);
	}
}
