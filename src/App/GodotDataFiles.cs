using System.IO;
using Godot;
using IdleLineage.Data;

namespace IdleLineage.App;

public static class GodotDataFiles
{
	private static bool _installed;

	public static void EnsureInstalled()
	{
		if (!_installed)
		{
			_installed = true;
			DataFileSystem.Install(Exists, ReadAllText, ReadAllBytes);
		}
	}

	private static bool Exists(string path)
	{
		if (!DataFileSystem.IsVirtual(path))
		{
			return File.Exists(path);
		}
		return Godot.FileAccess.FileExists(path);
	}

	private static string ReadAllText(string path)
	{
		if (!DataFileSystem.IsVirtual(path))
		{
			return File.ReadAllText(path);
		}
		using Godot.FileAccess fileAccess = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
		if (fileAccess == null)
		{
			throw Missing(path);
		}
		return fileAccess.GetAsText();
	}

	private static byte[] ReadAllBytes(string path)
	{
		if (!DataFileSystem.IsVirtual(path))
		{
			return File.ReadAllBytes(path);
		}
		using Godot.FileAccess fileAccess = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
		if (fileAccess == null)
		{
			throw Missing(path);
		}
		return fileAccess.GetBuffer((long)fileAccess.GetLength());
	}

	private static IOException Missing(string path)
	{
		return new IOException($"Unable to read '{path}': {Godot.FileAccess.GetOpenError()}.");
	}
}
