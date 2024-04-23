using System.Collections.Generic;
using System.Linq;
using IntervalTree;
using Microsoft.Dafny.LanguageServer.Language;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Microsoft.Dafny.LanguageServer.Workspace;

public class SymbolTable {

  public static SymbolTable Empty() {
    return new SymbolTable();
  }

  private SymbolTable() {
    NodePositions = new IntervalTree<Position, IDeclarationOrUsage>();
    Usages = new Dictionary<IDeclarationOrUsage, ISet<IDeclarationOrUsage>>();
    Declarations = new Dictionary<IDeclarationOrUsage, IDeclarationOrUsage>();
  }

  public SymbolTable(Compilation compilation, IReadOnlyList<(IDeclarationOrUsage usage, IDeclarationOrUsage declaration)> usages) {
    var safeUsages = usages.Where(k => k.declaration.NameToken.Uri != null).ToList();
    Declarations = safeUsages.DistinctBy(k => k.usage).
      ToDictionary(k => k.usage, k => k.declaration);
    Usages = safeUsages.GroupBy(u => u.declaration).ToDictionary(
      g => g.Key,
      g => (ISet<IDeclarationOrUsage>)g.Select(k => k.usage).ToHashSet());
    NodePositions = new IntervalTree<Position, IDeclarationOrUsage>();
    var symbols = safeUsages.Select(u => u.declaration).Concat<IDeclarationOrUsage>(usages.Select(u => u.usage)).
      Where(u => u.NameToken.Uri == compilation.Uri.ToUri()
                                       && !AutoGeneratedToken.Is(u.NameToken)).Distinct();
    foreach (var symbol in symbols) {
      var range = symbol.NameToken.GetLspRange();
      NodePositions.Add(range.Start, range.End, symbol);
    }
  }

  private IIntervalTree<Position, IDeclarationOrUsage> NodePositions { get; }
  private Dictionary<IDeclarationOrUsage, IDeclarationOrUsage> Declarations { get; }
  private Dictionary<IDeclarationOrUsage, ISet<IDeclarationOrUsage>> Usages { get; }

  public ISet<Location> GetUsages(Position position) {
    return NodePositions.Query(position).
      SelectMany(node => Usages.GetOrDefault(node, () => (ISet<IDeclarationOrUsage>)new HashSet<IDeclarationOrUsage>())).
      Select(u => new Location { Uri = u.NameToken.Filepath, Range = u.NameToken.GetLspRange() }).ToHashSet();
  }

  public Location? GetDeclaration(Position position) {
    var referenceNodes = NodePositions.Query(position);
    return referenceNodes.Select(node => Declarations.GetOrDefault(node, () => (IDeclarationOrUsage?)null))
      .Where(x => x != null).Select(
        n => new Location {
          Uri = DocumentUri.From(n!.NameToken.Uri),
          Range = n.NameToken.GetLspRange()
        }).FirstOrDefault();
  }
}