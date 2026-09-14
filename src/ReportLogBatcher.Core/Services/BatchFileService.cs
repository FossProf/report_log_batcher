namespace ReportLogBatcher.Core.Services;

public sealed record FileNameValidationResult(bool IsValid, string? ErrorMessage, string? FinalFileName);

public enum RenameFailureReason
{
    None,
    InvalidFileName,
    SourceMissing,
    Collision,
    FileSystem,
}

public sealed record RenameFileResult(
    bool IsSuccess,
    string? ErrorMessage,
    string? SourcePath,
    string? DestinationPath,
    string? DestinationFileName,
    RenameFailureReason FailureReason = RenameFailureReason.None);

public sealed class BatchFileService
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public FileNameValidationResult ValidateFileName(string? proposedFileName)
    {
        if (string.IsNullOrWhiteSpace(proposedFileName))
            return new FileNameValidationResult(false, "The file name cannot be empty.", null);

        if (proposedFileName.IndexOf('\\') >= 0 || proposedFileName.IndexOf('/') >= 0)
            return new FileNameValidationResult(false, "The file name must not contain directory separators.", null);

        if (proposedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return new FileNameValidationResult(false, "The file name contains invalid characters.", null);

        var hasDocxExtension = proposedFileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
        var finalName = hasDocxExtension ? proposedFileName : proposedFileName + ".docx";

        var stem = finalName.Length >= ".docx".Length
            ? finalName[..^".docx".Length]
            : finalName;

        if (stem == "." || stem == "..")
            return new FileNameValidationResult(false, "The file name is not allowed.", null);

        if (stem.EndsWith('.') || stem.EndsWith(' '))
            return new FileNameValidationResult(false, "The file name cannot end with a period or a space.", null);

        if (IsReservedDeviceName(stem))
            return new FileNameValidationResult(false, $"'{stem}' is a reserved Windows device name.", null);

        return new FileNameValidationResult(true, null, finalName);
    }

    public RenameFileResult RenameFile(string sourcePath, string proposedFileName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return Fail("The source file path is required.", RenameFailureReason.InvalidFileName, sourcePath);

        var validation = ValidateFileName(proposedFileName);
        if (!validation.IsValid)
            return Fail(validation.ErrorMessage!, RenameFailureReason.InvalidFileName, sourcePath);

        if (!File.Exists(sourcePath))
            return Fail("The source file does not exist.", RenameFailureReason.SourceMissing, sourcePath);

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(sourceDirectory))
            return Fail("The source file has no parent directory.", RenameFailureReason.FileSystem, sourcePath);

        var destinationPath = Path.Combine(sourceDirectory, validation.FinalFileName!);

        if (string.Equals(sourcePath, destinationPath, StringComparison.Ordinal))
            return new RenameFileResult(true, null, sourcePath, destinationPath, Path.GetFileName(destinationPath), RenameFailureReason.None);

        var isCaseOnlyRename =
            string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sourcePath, destinationPath, StringComparison.Ordinal);

        if (!isCaseOnlyRename && (File.Exists(destinationPath) || Directory.Exists(destinationPath)))
            return Fail(
                $"A file or folder named '{Path.GetFileName(destinationPath)}' already exists in the reports directory.",
                RenameFailureReason.Collision,
                sourcePath);

        if (isCaseOnlyRename)
        {
            var temporaryPath = CreateUnusedTemporaryPath(sourceDirectory);
            try
            {
                File.Move(sourcePath, temporaryPath);
            }
            catch (Exception)
            {
                return Fail("Could not rename the file; the source file may be locked or unavailable.",
                    RenameFailureReason.FileSystem, sourcePath);
            }

            try
            {
                File.Move(temporaryPath, destinationPath);
            }
            catch (Exception)
            {
                TryRestore(temporaryPath, sourcePath);
                return Fail("Could not complete the case-only rename; the original file was restored.",
                    RenameFailureReason.FileSystem, sourcePath);
            }

            return Success(sourcePath, destinationPath);
        }

        try
        {
            File.Move(sourcePath, destinationPath);
        }
        catch (Exception)
        {
            return Fail("Could not rename the file; it may be locked or the destination became unavailable.",
                RenameFailureReason.FileSystem, sourcePath);
        }

        return Success(sourcePath, destinationPath);
    }

    private static bool IsReservedDeviceName(string stem) => ReservedDeviceNames.Contains(stem);

    private static RenameFileResult Fail(string message, RenameFailureReason reason, string sourcePath) =>
        new(false, message, sourcePath, null, null, reason);

    private static RenameFileResult Success(string sourcePath, string destinationPath) =>
        new(true, null, sourcePath, destinationPath, Path.GetFileName(destinationPath), RenameFailureReason.None);

    private static void TryRestore(string fromPath, string toPath)
    {
        try
        {
            if (File.Exists(fromPath))
                File.Move(fromPath, toPath);
        }
        catch
        {
        }
    }

    private static string CreateUnusedTemporaryPath(string directory)
    {
        while (true)
        {
            var candidate = Path.Combine(directory, $"~rlb-rename-{Guid.NewGuid():N}.tmp");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }
}