using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;

namespace Microsoft.Dafny;

public class InferDecreasesClause {
  private readonly ModuleResolver resolver;

  public InferDecreasesClause(ModuleResolver resolver) {
    this.resolver = resolver;
  }

  public void FillInDefaultDecreasesClauses(ModuleDefinition module) {

    Contract.Assert(Type.GetScope() != null);
    foreach (var clbl in ModuleDefinition.AllCallables(module.TopLevelDecls)) {
      ICallable m;
      string s;
      if (clbl is ExtremeLemma) {
        var prefixLemma = ((ExtremeLemma)clbl).PrefixLemma;
        m = prefixLemma;
        s = prefixLemma.Name + " ";
      } else {
        m = clbl;
        s = "";
      }

      var anyChangeToDecreases = FillInDefaultDecreases(m, true);

      if (anyChangeToDecreases || m.InferredDecreases || m is PrefixLemma) {
        bool showIt = false;
        if (m is Function) {
          // show the inferred decreases clause only if it will ever matter, i.e., if the function is recursive
          showIt = ((Function)m).IsRecursive;
        } else if (m is PrefixLemma) {
          // always show the decrease clause, since at the very least it will start with "_k", which the programmer did not write explicitly
          showIt = true;
        } else {
          showIt = ((Method)m).IsRecursive;
        }

        if (showIt) {
          s += "decreases " + Util.Comma(m.Decreases.Expressions, expr => Printer.ExprToString(resolver.Options, expr));
          // Note, in the following line, we use the location information for "clbl", not "m".  These
          // are the same, except in the case where "clbl" is a GreatestLemma and "m" is a prefix lemma.
          resolver.reporter.Info(MessageSource.Resolver, clbl.Tok, s);
        }
      }
    }
  }

  /// <summary>
  /// Return "true" if this routine makes any change to the decreases clause.  If the decreases clause
  /// starts off essentially empty and a default is provided, then clbl.InferredDecreases is also set
  /// to true.
  /// </summary>
  public bool FillInDefaultDecreases(ICallable clbl, bool addPrefixInCoClusters) {
    Contract.Requires(clbl != null);

    if (clbl is ExtremePredicate) {
      // extreme predicates don't have decreases clauses
      return false;
    }

    var anyChangeToDecreases = false;
    var decr = clbl.Decreases.Expressions;
    if (decr.Count == 0 || (clbl is PrefixLemma && decr.Count == 1)) {
      // The default for a function starts with the function's reads clause, if any
      if (clbl is Function) {
        var fn = (Function)clbl;
        if (fn.Reads.Count != 0) {
          // start the default lexicographic tuple with the reads clause
          var r = FrameToObjectSet(fn.Reads);
          decr.Add(AutoGeneratedExpression.Create(r));
          anyChangeToDecreases = true;
        }
      }

      if (clbl is Function || clbl is Method) {
        TopLevelDeclWithMembers enclosingType;
        if (clbl is Function fc && !fc.IsStatic) {
          enclosingType = (TopLevelDeclWithMembers)fc.EnclosingClass;
        } else if (clbl is Method mc && !mc.IsStatic) {
          enclosingType = (TopLevelDeclWithMembers)mc.EnclosingClass;
        } else {
          enclosingType = null;
        }

        if (enclosingType != null) {
          var receiverType = ModuleResolver.GetThisType(clbl.Tok, enclosingType);
          if (receiverType.IsOrdered && !receiverType.IsRefType) {
            var th = new ThisExpr(clbl.Tok) { Type = receiverType }; // resolve here
            decr.Add(AutoGeneratedExpression.Create(th));
            anyChangeToDecreases = true;
          }
        }
      }

      // Add one component for each parameter, unless the parameter's type is one that
      // doesn't appear useful to orderings.
      foreach (var p in clbl.Ins) {
        if (!(p is ImplicitFormal) && p.Type.IsOrdered) {
          var ie = new IdentifierExpr(new AutoGeneratedToken(p.tok), p.Name);
          ie.Var = p;
          ie.Type = p.Type; // resolve it here
          decr.Add(AutoGeneratedExpression.Create(ie));
          anyChangeToDecreases = true;
        }
      }

      clbl.InferredDecreases = true; // this indicates that finding a default decreases clause was attempted
    }

    if (addPrefixInCoClusters && clbl is Function) {
      var fn = (Function)clbl;
      switch (fn.CoClusterTarget) {
        case Function.CoCallClusterInvolvement.None:
          break;
        case Function.CoCallClusterInvolvement.IsMutuallyRecursiveTarget:
          // prefix: decreases 0,
          clbl.Decreases.Expressions.Insert(0, Expression.CreateIntLiteral(fn.tok, 0));
          anyChangeToDecreases = true;
          break;
        case Function.CoCallClusterInvolvement.CoRecursiveTargetAllTheWay:
          // prefix: decreases 1,
          clbl.Decreases.Expressions.Insert(0, Expression.CreateIntLiteral(fn.tok, 1));
          anyChangeToDecreases = true;
          break;
        default:
          Contract.Assume(false); // unexpected case
          break;
      }
    }

    return anyChangeToDecreases;
  }

  public Expression FrameToObjectSet(List<FrameExpression> fexprs) {
    Contract.Requires(fexprs != null);
    Contract.Ensures(Contract.Result<Expression>() != null);

    List<Expression> sets = new List<Expression>();
    List<Expression> singletons = null;
    var idGen = new FreshIdGenerator();
    foreach (FrameExpression fe in fexprs) {
      Contract.Assert(fe != null);
      if (fe.E is WildcardExpr) {
        // drop wildcards altogether
      } else {
        Expression e = fe.E; // keep only fe.E, drop any fe.Field designation
        Contract.Assert(e.Type != null); // should have been resolved already
        var eType = e.Type.NormalizeExpand();
        if (eType.IsRefType) {
          // e represents a singleton set
          if (singletons == null) {
            singletons = new List<Expression>();
          }

          singletons.Add(e);
        } else if (eType is SeqType || eType is MultiSetType || LiteralExpr.IsEmptySet(e)) {
          // e represents a sequence or multiset
          var collectionType = (CollectionType)eType;
          var resolvedOpcode = collectionType.ResolvedOpcodeForIn;
          var boundedPool = collectionType.GetBoundedPool(e);

          // Add:  set x :: x in e
          var bv = new BoundVar(e.tok, idGen.FreshId("_s2s_"),
            collectionType.Arg.NormalizeExpand());
          var bvIE = new IdentifierExpr(e.tok, bv.Name) {
            Var = bv,
            Type = bv.Type
          };
          var sInE = new BinaryExpr(e.tok, BinaryExpr.Opcode.In, bvIE, e) {
            ResolvedOp = resolvedOpcode,
            Type = Type.Bool
          };
          var s = new SetComprehension(e.tok, e.RangeToken, true, new List<BoundVar>() { bv }, sInE, bvIE,
            new Attributes("trigger", new List<Expression> { sInE }, null)) {
            Type = new SetType(true, resolver.SystemModuleManager.ObjectQ()),
            Bounds = new List<ComprehensionExpr.BoundedPool>() { boundedPool }
          };
          sets.Add(s);
        } else {
          // e is already a set
          Contract.Assert(eType is SetType);
          sets.Add(e);
        }
      }
    }

    if (singletons != null) {
      Expression display = new SetDisplayExpr(singletons[0].tok, true, singletons);
      display.Type = new SetType(true, resolver.SystemModuleManager.ObjectQ()); // resolve here
      sets.Add(display);
    }

    if (sets.Count == 0) {
      Expression emptyset = new SetDisplayExpr(Token.NoToken, true, new List<Expression>());
      emptyset.Type = new SetType(true, resolver.SystemModuleManager.ObjectQ()); // resolve here
      return emptyset;
    } else {
      Expression s = sets[0];
      for (int i = 1; i < sets.Count; i++) {
        BinaryExpr union = new BinaryExpr(s.tok, BinaryExpr.Opcode.Add, s, sets[i]);
        union.ResolvedOp = BinaryExpr.ResolvedOpcode.Union; // resolve here
        union.Type = new SetType(true, resolver.SystemModuleManager.ObjectQ()); // resolve here
        s = union;
      }

      return s;
    }
  }
}