﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MAB.DotIgnore
{
    /// <summary>
    /// A list of ignore rules.
    /// </summary>
    public class IgnoreList
    {
        private readonly Action<string, IgnoreRule, IgnoreLog> _logAction =
            (path, rule, log) => {
                if (!log.ContainsKey(path))
                {
                    log.Add(path, new List<string>());
                }

                var state = rule.PatternFlags.HasFlag(PatternFlags.NEGATION) ? "INCLUDED" : "IGNORED";

                log[path].Add($"{state} by {rule}");
            };

        private readonly Action<string, IgnoreRule, IgnoreLog> _noAction =
            (path, rule, log) => { };

        private List<IgnoreRule> _rules = new List<IgnoreRule>();

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnoreList"/> class.
        /// </summary>
        /// <param name="rules">A list of glob ignore patterns as strings.</param>
        /// <param name="flags">Optional flags determining pattern matching behaviour.</param>
        public IgnoreList(IEnumerable<string> rules, MatchFlags flags = MatchFlags.PATHNAME) =>
            AddRules(rules, flags);

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnoreList"/> class.
        /// </summary>
        /// <param name="ignoreFilePath">Path to a text file containing a list of ignore patterns as strings.</param>
        /// <param name="flags">Optional flags determining pattern matching behaviour.</param>
        public IgnoreList(string ignoreFilePath, MatchFlags flags = MatchFlags.PATHNAME) =>
            AddRules(ignoreFilePath, flags);

        /// <summary>
        /// Gets the individual ignore rules currently loaded into this list.
        /// </summary>
        public ReadOnlyCollection<IgnoreRule> Rules =>
            _rules.AsReadOnly();

        /// <summary>
        /// Add a rule to the ignore list.
        /// </summary>
        /// <param name="rule">Glob ignore pattern as string.</param>
        /// <param name="flags">Optional flags determining pattern matching behaviour.</param>
        public void AddRule(string rule, MatchFlags flags = MatchFlags.PATHNAME) =>
            AddRules(new string[] { rule }, flags, false);

        /// <summary>
        /// Add multiple rules to the ignore list.
        /// </summary>
        /// <param name="ignoreFilePath">Path to a text file containing a list of glob ignore patterns as strings.</param>
        /// <param name="flags">Optional flags determining pattern matching behaviour.</param>
        public void AddRules(string ignoreFilePath, MatchFlags flags = MatchFlags.PATHNAME) =>
            AddRules(File.ReadAllLines(ignoreFilePath), flags, true);

        /// <summary>
        /// Add multiple rules to the ignore list.
        /// </summary>
        /// <param name="rules">A list of glob ignore patterns as strings.</param>
        /// <param name="flags">Optional flags determining pattern matching behaviour.</param>
        public void AddRules(IEnumerable<string> rules, MatchFlags flags = MatchFlags.PATHNAME) =>
            AddRules(rules, flags, false);

        /// <summary>
        /// Create an exact copy of the ignore list.
        /// </summary>
        /// <returns>An exact copy of the ignore list.</returns>
        public IgnoreList Clone()
        {
            var clone = new IgnoreList(new string[0]);

            _rules.ForEach(r => clone.AddRule(r.OriginalPattern, r.MatchFlags));

            return clone;
        }

        /// <summary>
        /// Check if a file path matches any of the rules in the ignore list.
        /// </summary>
        /// <param name="file">FileInfo representing the file to check.</param>
        /// <returns>True if the file path is ignored.</returns>
        public bool IsIgnored(FileInfo file) =>
            IsIgnored(file, null);

        /// <summary>
        /// Check if a directory path matches any of the rules in the ignore list.
        /// </summary>
        /// <param name="file">FileInfo representing the file to check.</param>
        /// <param name="log">List of strings to append log messages to.</param>
        /// <returns>True if the file path is ignored.</returns>
        public bool IsIgnored(FileInfo file, IgnoreLog log) =>
            IsIgnored(file.FullName, false, log);

        /// <summary>
        /// Check if a directory path matches any of the rules in the ignore list.
        /// </summary>
        /// <param name="directory">DirectoryInfo representing the file to check.</param>
        /// <returns>True if the directory path is ignored.</returns>
        public bool IsIgnored(DirectoryInfo directory) =>
            IsIgnored(directory, null);

        /// <summary>
        /// Check if a directory path matches any of the rules in the ignore list.
        /// </summary>
        /// <param name="directory">DirectoryInfo representing the file to check.</param>
        /// <param name="log">List of strings to append log messages to.</param>
        /// <returns>True if the directory path is ignored.</returns>
        public bool IsIgnored(DirectoryInfo directory, IgnoreLog log) =>
            IsIgnored(directory.FullName, true, log);

        /// <summary>
        /// Check if a string path matches any of the rules in the ignore list.
        /// </summary>
        /// <param name="path">String representing the path to check.</param>
        /// <param name="pathIsDirectory">Should be set True if the path represents a directory, False if it represents a file.</param>
        /// <returns>True if the file or directory path is ignored.</returns>
        public bool IsIgnored(string path, bool pathIsDirectory) =>
            IsIgnored(path, pathIsDirectory, null);

        /// <summary>
        /// Check if a string path matches any of the rules in the ignore list.
        /// </summary>
        /// <param name="path">String representing the path to check.</param>
        /// <param name="pathIsDirectory">Should be set True if the path represents a directory, False if it represents a file.</param>
        /// <param name="log">List of strings to append log messages to.</param>
        /// <returns>True if the file or directory path is ignored.</returns>
        public bool IsIgnored(string path, bool pathIsDirectory, IgnoreLog log)
        {
            var pathIgnored = IsPathIgnored(path, pathIsDirectory, log);

            if (pathIsDirectory)
            {
                return pathIgnored;
            }

            var ancestorIgnored = IsAnyParentDirectoryIgnored(path, log);

            if (ancestorIgnored)
            {
                return true;
            }

            return pathIgnored;
        }

        /// <summary>
        /// Remove a rule from the ignore list.
        /// </summary>
        /// <param name="rule">Glob ignore pattern as string.</param>
        public void RemoveRule(string rule) =>
            _rules.RemoveAll(r => r.OriginalPattern == rule.Trim());

        private void AddRules(IEnumerable<string> rules, MatchFlags flags, bool loadedFromFile) =>
            _rules.AddRange(GetRuleLines(rules, loadedFromFile)
                .Select(line => new IgnoreRule(line.Pattern, flags, line.LineNumber)));

        private IEnumerable<RuleLine> GetRuleLines(IEnumerable<string> rules, bool lineNumbers) =>

            // Exclude all comment or whitespace lines
            // Note that we store the line numbers (if flag set) *before* filtering out
            // comments and whitespace, otherwise they don't match up with the source file
            rules.Select((line, index) => new RuleLine { LineNumber = lineNumbers ? (index + 1) : default(int?), Pattern = line.Trim() })
                        .Where(line => line.Pattern.Length > 0 && !line.Pattern.StartsWith("#", StringComparison.OrdinalIgnoreCase));

        private bool IsAnyParentDirectoryIgnored(string path, IgnoreLog log)
        {
            var segments = Utils.NormalisePath(path).Split('/').ToList();

            segments.RemoveAt(segments.Count - 1);

            var directory = new List<string>();

            // Loop over all the path segments (moving down the directory tree)
            // and test each as a directory, returning immediately if true
            foreach (var segment in segments)
            {
                directory.Add(segment);

                if (IsPathIgnored(string.Join("/", directory.ToArray()), true, log))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsPathIgnored(string path, bool pathIsDirectory, IgnoreLog log)
        {
            var logAction = (log != null) ? _logAction : _noAction;

            // This pattern modified from https://github.com/henon/GitSharp/blob/master/GitSharp/IgnoreRules.cs
            var ignore = false;

            foreach (var rule in _rules)
            {
                if (rule.IsMatch(path, pathIsDirectory))
                {
                    ignore = !rule.PatternFlags.HasFlag(PatternFlags.NEGATION);

                    logAction(path, rule, log);
                }
            }

            return ignore;
        }
    }
}
