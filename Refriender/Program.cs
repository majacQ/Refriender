﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using RefrienderCore;

namespace Refriender {
	class Program {
		static readonly Dictionary<string, CompressionAlgorithm> Algorithms = new() {
			["deflate"] = CompressionAlgorithm.Deflate, 
			["zlib"] = CompressionAlgorithm.Zlib,
			["gzip"] = CompressionAlgorithm.Gzip,
			["bzip2"] = CompressionAlgorithm.Bzip2, 
			["lzma"] = CompressionAlgorithm.Lzma, 
			["lzma2"] = CompressionAlgorithm.Lzma2, 
			["lzw"] = CompressionAlgorithm.Lzw, 
			["all"] = CompressionAlgorithm.All, 
		};

		static readonly Dictionary<CompressionAlgorithm, string> RevAlgorithms =
			Algorithms.ToDictionary(x => x.Value, x => x.Key);

		public class Options {
			[Option('q', "quiet", Required = false, HelpText = "Silence messages")]
			public bool Quiet { get; set; }
			[Option('v', "verbose", Required = false, HelpText = "Verbose messages")]
			public bool Verbose { get; set; }
			[Option('s', "start-only", Required = false, HelpText = "Only find starting positions of blocks")]
			public bool StartOnly { get; set; }
			[Option('p', "preserve-overlapping", Required = false, HelpText = "Preserve overlapping blocks")]
			public bool PreserveOverlapping { get; set; }
			[Option('e', "extract-to", Required = false, HelpText = "Path for extraction")]
			public string ExtractTo { get; set; }
			[Option('a', "algorithms", Required = false, Default = "all", HelpText = "Comma-separated list of algorithms (valid options: all (!SLOW!), deflate, zlib, gzip, bzip2, lzma, lzma2, lzw)")]
			public string Algorithms { get; set; }
			[Option('f', "find-pointers", Required = false, HelpText = "Comma-separated list of offsets/ranges in which to search for pointers to blocks (e.g. 0,4,8,16 or 1-8,32)")]
			public string FindPointers { get; set; }
			[Option('m', "min-length", Required = false, Default = 1, HelpText = "Minimum decompressed block length")]
			public int MinLength { get; set; }
			[Value(0, Required = true, MetaName = "filename", HelpText = "File to scan")]
			public string Filename { get; set; }
		}
		static void Main(string[] args) {
			Parser.Default.ParseArguments<Options>(args).WithParsed(opt => {
				if(opt.Quiet && (opt.StartOnly || opt.Verbose || opt.FindPointers != null || opt.ExtractTo == null))
					throw new NotSupportedException(); // TODO: Error messages
				var algorithms = (CompressionAlgorithm) opt.Algorithms.Split(',')
					.Select(x => (int) Algorithms[x.Trim().ToLower()]).Sum();
				var data = File.ReadAllBytes(opt.Filename);
				var cf = new CompressionFinder(data, minLength: opt.MinLength, algorithms: algorithms, positionOnly: opt.StartOnly, removeOverlapping: !opt.PreserveOverlapping, logLevel: opt.Quiet ? 0 : opt.Verbose ? 2 : 1);
				if(opt.StartOnly) {
					foreach(var (algorithm, offset) in cf.StartingPositions.OrderBy(x => x.Offset))
						Console.WriteLine($"[{RevAlgorithms[algorithm]}] Block starts at 0x{offset:X}");
					Console.WriteLine($"{cf.StartingPositions.Count} starting positions found");
				} else {
					if(!opt.Quiet) {
						foreach(var block in cf.Blocks.OrderBy(x => x.Offset))
							Console.WriteLine($"[{RevAlgorithms[block.Algorithm]}] 0x{block.Offset:X} - 0x{block.Offset + block.CompressedLength:X} (compressed length 0x{block.CompressedLength:X}, decompressed length 0x{block.DecompressedLength:X}");
						Console.WriteLine($"{cf.Blocks.Count} blocks found");
					}

					if(opt.FindPointers != null) {
						var offsets = opt.FindPointers.Split(',').Select(x => {
							var a = x.Split('-');
							var start = int.Parse(a[0]);
							return a.Length == 2
								? Enumerable.Range(start, int.Parse(a[1]) - start + 1)
								: new[] { start };
						}).SelectMany(x => x);
						foreach(var offset in offsets) {
							if(opt.Verbose)
								Console.WriteLine($"Finding pointers to {offset} bytes before the blocks");
							foreach(var block in cf.Blocks.OrderBy(x => x.Offset)) {
								var pointers = cf.FindPointers(block.Offset - offset).ToList();
								if(pointers.Count != 0)
									Console.WriteLine($"Block 0x{block.Offset:X}{(offset != 0 ? $" (- {offset} == 0x{block.Offset - offset:X})" : "")} has pointers from: {string.Join(", ", pointers.Select(x => $"0x{x:X}"))}");
							}
						}
					}

					if(opt.ExtractTo == null) return;
					if(opt.Verbose) Console.WriteLine("Beginning block extraction");
					Directory.CreateDirectory(opt.ExtractTo);
					foreach(var block in cf.Blocks) {
						var fn = $"0x{block.Offset:X}-0x{block.Offset+block.CompressedLength:X}_{RevAlgorithms[block.Algorithm]}.bin";
						var bdata = cf.Decompress(block);
						File.WriteAllBytes(Path.Join(opt.ExtractTo, fn), bdata);
					}
					if(opt.Verbose) Console.WriteLine("Done!");
				}
			});
		}
	}
}