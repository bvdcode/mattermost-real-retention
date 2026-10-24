namespace Mattermost.RealRetention.Services;

public sealed class LocalFileStorage(string directory)
{
    private readonly string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    public void ValidateRoot(bool requireNonEmpty)
    {
        if (!System.IO.Directory.Exists(root))
            throw new DirectoryNotFoundException($"The data directory {root} does not exist.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The data directory must not be a symbolic link.");
        if (requireNonEmpty && !System.IO.Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException("The data directory is empty. Check the Mattermost volume mount before deleting metadata.");
    }

    public string Resolve(string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\\') ||
            relativePath.Split('/').Any(part => part is ".." or "." or ""))
            throw new IOException("An attachment path is not a safe relative path.");

        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("An attachment path escapes the data directory.");
        ValidateComponents(path);
        return path;
    }

    public (bool Exists, long Length) Process(string path, bool dryRun)
    {
        // Recheck after any configured delay; never follow links in the filestore.
        ValidateComponents(path);
        try
        {
            var length = new FileInfo(path).Length;
            if (!dryRun)
                File.Delete(path);
            return (true, length);
        }
        catch (FileNotFoundException)
        {
            return (false, 0);
        }
        catch (DirectoryNotFoundException)
        {
            return (false, 0);
        }
    }

    private void ValidateComponents(string path)
    {
        var current = root;
        foreach (var component in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("An attachment path contains a symbolic link.");
            if (current == path && (attributes & FileAttributes.Directory) != 0)
                throw new IOException("An attachment path points to a directory instead of a file.");
        }
    }
}
