namespace NetDeploy.Core.Logs;

/// <summary>How far a search of every log has got: the log just searched and its matches, if one was.</summary>
public sealed record LogSearchProgress(double Fraction, AppLog? Searched = null, int Matches = 0);
