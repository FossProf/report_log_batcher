using ReportLogBatcher.Core.Models;

namespace ReportLogBatcher.Core.Services;

/// <summary>
/// Deterministic SPIN-report parser contract.
///
/// Implementations MUST:
/// - open source files read-only and never modify them;
/// - preserve source wording verbatim (see <see cref="SourceTextPolicy"/>);
/// - never invent missing values or substitute <c>N/A</c>;
/// - leave every field they cannot resolve as null and report why via
///   <see cref="SpinParseResult.Issues"/>.
///
/// Core stays independent of Open XML; the implementation lives in
/// ReportLogBatcher.Infrastructure.
/// </summary>
public interface ISpinReportParser
{
    SpinParseResult Parse(string spinReportPath);
}