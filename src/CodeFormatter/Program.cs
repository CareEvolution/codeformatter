﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.DotNet.CodeFormatting;
using System.Diagnostics;
using Buildalyzer;
using Buildalyzer.Workspaces;

namespace CodeFormatter
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var result = CommandLineParser.Parse(args);
            if (result.IsError)
            {
                Console.Error.WriteLine(result.Error);
                CommandLineParser.PrintUsage();
                return -1;
            }

            var options = result.Options;
            int exitCode;
            switch (options.Operation)
            {
                case Operation.ListRules:
                    RunListRules();
                    exitCode = 0;
                    break;
                case Operation.Format:
                    exitCode = RunFormat(options);
                    break;
                default:
                    throw new Exception("Invalid enum value: " + options.Operation);
            }

            return 0;
        }

        private static void RunListRules()
        {
            var rules = FormattingEngine.GetFormattingRules();
            Console.WriteLine("{0,-20} {1}", "Name", "Description");
            Console.WriteLine("==============================================");
            foreach (var rule in rules)
            {
                Console.WriteLine("{0,-20} :{1}", rule.Name, rule.Description);
            }
        }

        private static int RunFormat(CommandLineOptions options)
        {
            var cts = new CancellationTokenSource();
            var ct = cts.Token;

            Console.CancelKeyPress += delegate { cts.Cancel(); };

            try
            {
                RunFormatAsync(options, ct).Wait(ct);
                Console.WriteLine("Completed formatting.");
                return 0;
            }
            catch (AggregateException ex)
            {
                var typeLoadException = ex.InnerExceptions.FirstOrDefault() as ReflectionTypeLoadException;
                if (typeLoadException == null)
                    throw;

                Console.WriteLine("ERROR: Type loading error detected. In order to run this tool you need either Visual Studio 2015 or Microsoft Build Tools 2015 tools installed.");
                var messages = typeLoadException.LoaderExceptions.Select(e => e.Message).Distinct();
                foreach (var message in messages)
                    Console.WriteLine("- {0}", message);

                return 1;
            }
        }

        private static async Task<int> RunFormatAsync(CommandLineOptions options, CancellationToken cancellationToken)
        {
            var engine = FormattingEngine.Create();
            engine.PreprocessorConfigurations = options.PreprocessorConfigurations;
            engine.FileNames = options.FileNames;
            engine.CopyrightHeader = options.CopyrightHeader;
            engine.AllowTables = options.AllowTables;
            engine.Verbose = options.Verbose;

            if (!SetRuleMap(engine, options.RuleMap))
            {
                return 1;
            }

            foreach (var item in options.FormatTargets)
            {
                await RunFormatItemAsync(engine, item, options.Language, cancellationToken);
            }

            return 0;
        }

        private static async Task RunFormatItemAsync(IFormattingEngine engine, string item, string language, CancellationToken cancellationToken)
        {
            Console.WriteLine(Path.GetFileName(item));
            var manager = new AnalyzerManager(item);
            string extension = Path.GetExtension(item);
            using (var workspace = manager.GetWorkspace())
            {
                await engine.FormatSolutionAsync(workspace.CurrentSolution, cancellationToken);

            }
        }

        // private static void ThrowIfDiagnostics(AdhocWorkspace workspace)
        // {
        //     foreach (var diagnostic in workspace.)
        //     {
        //         Console.WriteLine($"{diagnostic.Kind} {diagnostic.Message}");
        //     }
        //     if (workspace.Diagnostics.Count > 0) Environment.Exit(1);
        // }

        private static bool SetRuleMap(IFormattingEngine engine, ImmutableDictionary<string, bool> ruleMap)
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            foreach (var entry in ruleMap)
            {
                var rule = engine.AllRules.Where(x => comparer.Equals(x.Name, entry.Key)).FirstOrDefault();
                if (rule == null)
                {
                    Console.WriteLine("Could not find rule with name {0}", entry.Key);
                    return false;
                }

                engine.ToggleRuleEnabled(rule, entry.Value);
            }

            return true;
        }
    }
}
