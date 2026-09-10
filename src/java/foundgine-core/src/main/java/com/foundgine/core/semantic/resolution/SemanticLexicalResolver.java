package com.foundgine.core.semantic.resolution;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.AliasWeightEvidenceGate;
import com.foundgine.core.semantic.AliasWeightEvidenceGate.AliasEvidenceStatus;
import com.foundgine.core.semantic.AliasWeightEvidenceGate.AliasInterpretationEvidence;
import com.foundgine.core.semantic.SemanticContractSnapshot;
import java.util.*;

/** Resolves lexical tokens into graph-constrained semantic interpretations. */
public final class SemanticLexicalResolver {
    private final SemanticContractSnapshot contract;
    private final ISemanticLexicalCandidateSource source;
    private final int candidateLimit, maxBridgeHops, maxTokens, maxPathsExplored;
    private final double ambiguityThreshold;
    private final long timeoutMillis, retrievalTimeoutMillis;
    private final Integer minimumAliasWeight;

    public SemanticLexicalResolver(SemanticContractSnapshot contract, ISemanticLexicalCandidateSource source) {
        this(contract, source, 20, 4, .03, 32, 5000, 250, 2000, null);
    }
    public SemanticLexicalResolver(SemanticContractSnapshot contract, ISemanticLexicalCandidateSource source,
            int candidateLimit, int maxBridgeHops, double ambiguityThreshold, int maxTokens,
            int maxPathsExplored, long timeoutMillis, long retrievalTimeoutMillis, Integer minimumAliasWeight) {
        this.contract=Objects.requireNonNull(contract); this.source=Objects.requireNonNull(source);
        if(candidateLimit<1||candidateLimit>1000) throw new IllegalArgumentException("candidateLimit out of range");
        if(maxBridgeHops<0||maxBridgeHops>16) throw new IllegalArgumentException("maxBridgeHops out of range");
        if(ambiguityThreshold<0||ambiguityThreshold>1) throw new IllegalArgumentException("ambiguityThreshold out of range");
        if(maxTokens<1||maxTokens>256) throw new IllegalArgumentException("maxTokens out of range");
        if(maxPathsExplored<1||maxPathsExplored>1_000_000) throw new IllegalArgumentException("maxPathsExplored out of range");
        if(timeoutMillis<=0||retrievalTimeoutMillis<=0) throw new IllegalArgumentException("timeouts must be positive");
        if(minimumAliasWeight!=null&&(minimumAliasWeight<1||minimumAliasWeight>100)) throw new IllegalArgumentException("minimumAliasWeight out of range");
        this.candidateLimit=candidateLimit;this.maxBridgeHops=maxBridgeHops;this.ambiguityThreshold=ambiguityThreshold;
        this.maxTokens=maxTokens;this.maxPathsExplored=maxPathsExplored;this.timeoutMillis=timeoutMillis;
        this.retrievalTimeoutMillis=retrievalTimeoutMillis;this.minimumAliasWeight=minimumAliasWeight;
    }
    public SemanticLexicalResolution resolve(String expression) { return resolve(expression, ()->false); }
    public SemanticLexicalResolution resolve(String expression, ISemanticLexicalCandidateSource.CancellationToken cancellation) {
        var d=ground(expression,cancellation);
        if(d.outcome()==GroundingOutcome.UNRESOLVED) return new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.UNRESOLVED,List.of(),0,null,d.reason(),d.rootCandidates());
        if(d.outcome()==GroundingOutcome.BUDGET_EXCEEDED) return new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.BUDGET_EXCEEDED,List.of(),0,null,d.reason(),d.rootCandidates());
        var leading=d.committed()!=null?d.committed():d.competingInterpretations().get(0);
        var outcome=d.outcome()==GroundingOutcome.REQUIRES_CLARIFICATION?SemanticLexicalResolutionOutcome.AMBIGUOUS:SemanticLexicalResolutionOutcome.RESOLVED;
        return new SemanticLexicalResolution(outcome,leading.steps(),leading.interpretationScore(),leading.rootEntity(),d.reason(),d.rootCandidates());
    }
    public GroundingDecision ground(String expression) { return ground(expression, ()->false); }
    public GroundingDecision ground(String expression, ISemanticLexicalCandidateSource.CancellationToken cancellation) {
        if(expression==null||expression.isBlank()) throw new IllegalArgumentException("Lexical expression cannot be empty.");
        var tokens=tokenize(expression);
        if(tokens.length==0) return new GroundingDecision(expression,GroundingOutcome.UNRESOLVED,null,List.of(),"No lexical tokens were found.",List.of());
        if(tokens.length>maxTokens) return new GroundingDecision(expression,GroundingOutcome.BUDGET_EXCEEDED,null,List.of(),"Expression has "+tokens.length+" tokens, exceeding the configured maximum of "+maxTokens+". Grounding was refused before retrieval or graph search.",List.of(),GroundingBudgetLimit.MAX_TOKENS,List.of(),null,List.of());
        var trunc=new LinkedHashMap<String,CandidateTruncation>();
        Map<String,List<SemanticLexicalCandidate>> sets;
        long retrievalStart=System.nanoTime();
        try { sets=getCandidates(Arrays.asList(tokens), cancellation, retrievalStart, true, trunc); }
        catch(GroundingRetrievalTimeoutException e) { return budgetDecision(expression,"Candidate retrieval for token '"+e.token()+"' exceeded the configured retrieval timeout.",GroundingBudgetLimit.RETRIEVAL_TIMEOUT); }
        catch(Cancelled e) { return budgetDecision(expression,"Candidate retrieval was cancelled before the graph search began.",GroundingBudgetLimit.CANCELLED); }
        if(sets.values().stream().anyMatch(List::isEmpty) && tokens.length>1) {
            String compact=String.join("",tokens); var ct=new LinkedHashMap<String,CandidateTruncation>();
            try { var compactSets=getCandidates(List.of(compact),cancellation,retrievalStart,false,ct); if(!compactSets.getOrDefault(compact,List.of()).isEmpty()){tokens=new String[]{compact};sets=compactSets;trunc=ct;} }
            catch(GroundingRetrievalTimeoutException e){return budgetDecision(expression,"Candidate retrieval for compact token '"+e.token()+"' exceeded the configured retrieval timeout.",GroundingBudgetLimit.RETRIEVAL_TIMEOUT);}
            catch(Cancelled e){return budgetDecision(expression,"Candidate retrieval was cancelled before the graph search began.",GroundingBudgetLimit.CANCELLED);}
        }
        for(String t:tokens) if(sets.getOrDefault(t,List.of()).isEmpty()) return new GroundingDecision(expression,GroundingOutcome.UNRESOLVED,null,List.of(),"No lexical candidate was returned for token '"+t+"'.",List.of());
        var roots=new ArrayList<>(sets.get(tokens[0]));
        roots.sort(candidateComparator());
        var budget=new SearchBudget(maxPathsExplored,timeoutMillis,cancellation);
        var raw=new ArrayList<SemanticLexicalResolution>();
        for(var root:roots){if(budget.exceeded)break;var state=createRootState(tokens[0],root);if(state!=null)search(tokens,1,sets,state,raw,budget);}
        if(budget.exceeded){var partial=distinctInterpretations(raw);return new GroundingDecision(expression,GroundingOutcome.BUDGET_EXCEEDED,null,List.of(),"Grounding stopped ("+budget.limitHit+") before every candidate interpretation could be explored. "+partial.size()+" partial interpretation(s) had been found at cutoff; none was committed.",roots,budget.limitHit,partial,null,List.of());}
        if(raw.isEmpty()) return new GroundingDecision(expression,GroundingOutcome.UNRESOLVED,null,List.of(),"No complete semantic path could be constructed from the lexical candidates.",roots);
        var interpretations=distinctInterpretations(raw); var best=interpretations.get(0);
        if(minimumAliasWeight!=null && best.effectiveAliasEvidence().status()==AliasEvidenceStatus.INSUFFICIENT)
            return new GroundingDecision(expression,GroundingOutcome.REQUIRES_CLARIFICATION,null,List.of(),"The leading interpretation contains declared lexical alias evidence below the configured minimum weight of "+minimumAliasWeight+".",roots,GroundingBudgetLimit.NONE,List.of(),best.effectiveAliasEvidence(),List.of());
        var close=interpretations.stream().filter(x->Math.abs(x.interpretationScore()-best.interpretationScore())<ambiguityThreshold).toList();
        if(close.size()<=1){var risks=best.steps().stream().map(SemanticLexicalStep::token).distinct().map(trunc::get).filter(Objects::nonNull).filter(x->x.withinAmbiguityMargin(ambiguityThreshold)).toList();
            if(!risks.isEmpty()) return new GroundingDecision(expression,GroundingOutcome.REQUIRES_CLARIFICATION,null,interpretations.subList(1,interpretations.size()),"Candidate retrieval was truncated within the ambiguity margin; Foundgine will not commit automatically.",roots,GroundingBudgetLimit.NONE,List.of(),best.effectiveAliasEvidence(),risks);
            return new GroundingDecision(expression,GroundingOutcome.COMMITTED,best,interpretations.subList(1,interpretations.size()),interpretations.size()>1?"One interpretation scored clearly above the others; remaining interpretations are retained as rejected alternatives.":"A single semantic interpretation was constructed and no competing meaning was found.",roots,GroundingBudgetLimit.NONE,List.of(),best.effectiveAliasEvidence(),List.of());}
        return new GroundingDecision(expression,GroundingOutcome.REQUIRES_CLARIFICATION,null,close,"Multiple semantically distinct interpretations are within the configured ambiguity margin.",roots,GroundingBudgetLimit.NONE,List.of(),best.effectiveAliasEvidence(),List.of());
    }
    public Map<String,List<SemanticLexicalCandidate>> getCandidates(String expression){ if(expression==null||expression.isBlank())throw new IllegalArgumentException("Lexical expression cannot be empty."); return getCandidates(Arrays.asList(tokenize(expression)),()->false,System.nanoTime(),false,new LinkedHashMap<>()); }
    private Map<String,List<SemanticLexicalCandidate>> getCandidates(List<String> tokens,ISemanticLexicalCandidateSource.CancellationToken cancellation,long start,boolean stopOnEmpty,Map<String,CandidateTruncation> trunc){
        var out=new LinkedHashMap<String,List<SemanticLexicalCandidate>>();
        for(String token:tokens){checkRetrieval(token,start,cancellation); List<SemanticLexicalCandidate> cs=source.retrieve(new SemanticLexicalRequest(token,null,null,candidateLimit+1),cancellation); checkRetrieval(token,start,cancellation);
            cs=cs.stream().filter(x->x.score()>=0).collect(java.util.stream.Collectors.toMap(this::identityKey,x->x,(a,b)->better(a,b),LinkedHashMap::new)).values().stream().sorted(candidateComparator()).toList();
            if(cs.size()>candidateLimit) trunc.put(token,new CandidateTruncation(token,candidateLimit,cs.size()-candidateLimit,cs.get(candidateLimit-1).score(),cs.get(candidateLimit).score()));
            cs=cs.stream().limit(candidateLimit).toList(); var exact=cs.stream().filter(x->(x.kind()==SemanticLexicalCandidateKind.ENTITY||x.kind()==SemanticLexicalCandidateKind.NODE)&&x.canonicalName().equalsIgnoreCase(token)).toList(); if(!exact.isEmpty())cs=exact; out.put(token,cs); if(stopOnEmpty&&cs.isEmpty())break; }
        return out;
    }
    private void checkRetrieval(String token,long start,ISemanticLexicalCandidateSource.CancellationToken c){if(c!=null&&c.isCancellationRequested())throw new Cancelled(); if(elapsed(start)>=retrievalTimeoutMillis)throw new GroundingRetrievalTimeoutException(token,elapsed(start));}
    private long elapsed(long start){return (System.nanoTime()-start)/1_000_000;}
    private void search(String[] tokens,int index,Map<String,List<SemanticLexicalCandidate>> candidates,SearchState state,List<SemanticLexicalResolution> results,SearchBudget budget){if(budget.tick())return;if(index==tokens.length){double score=state.steps.isEmpty()?0:Math.max(0,Math.min(1,state.steps.stream().mapToDouble(x->x.candidate().score()).average().orElse(0)*state.graphFactor));results.add(new SemanticLexicalResolution(SemanticLexicalResolutionOutcome.RESOLVED,state.steps,score,state.rootEntity,"A complete lexical path was grounded."));return;} for(var candidate:candidates.getOrDefault(tokens[index],List.of())){if(budget.tick())return;for(var transition:resolveTransition(state,candidate,budget)){if(budget.tick())return;search(tokens,index+1,candidates,state.add(new SemanticLexicalStep(tokens[index],candidate,transition.factor,transition.path),transition.factor),results,budget);if(budget.exceeded)return;}}}
    private SearchState createRootState(String token,SemanticLexicalCandidate c){EntityId root=null,current=null;switch(c.kind()){case ENTITY,NODE,FIELD,VALUE->{root=c.entityId();current=c.entityId();}case RELATIONSHIP,TRAVERSAL->{root=c.sourceEntityId();current=c.targetEntityId();}default->{}} if(root==null||current==null)return null;try{contract.get(root);contract.get(current);}catch(Exception e){return null;}return new SearchState(root,current,List.of(new SemanticLexicalStep(token,c,c.score(),List.of())),c.score());}
    private List<Transition> resolveTransition(SearchState state,SemanticLexicalCandidate c,SearchBudget budget){EntityId owner=switch(c.kind()){case ENTITY,NODE,FIELD,VALUE->c.entityId();case RELATIONSHIP,TRAVERSAL->c.sourceEntityId();default->null;};if(owner==null)return List.of();if(owner.equals(state.currentEntity))return List.of(new Transition(1,List.of()));var path=findPath(state.currentEntity,owner,maxBridgeHops,budget);return path.isEmpty()?List.of():List.of(new Transition(Math.pow(.9,path.size()),path));}
    private List<SemanticLexicalCandidate> findPath(EntityId sourceId,EntityId targetId,int maxHops,SearchBudget budget){if(sourceId.equals(targetId))return List.of();var q=new ArrayDeque<PathNode>();q.add(new PathNode(sourceId,List.of()));var visited=new HashSet<EntityId>();visited.add(sourceId);while(!q.isEmpty()){if(budget.tick())return List.of();var n=q.remove();if(n.path.size()>=maxHops)continue;for(var r:contract.get(n.entity).relationships()){var hop=new SemanticLexicalCandidate(r.name(),SemanticLexicalCandidateKind.RELATIONSHIP,r.name(),1,null,r.id(),null,n.entity,r.target(),null,List.of());var p=new ArrayList<>(n.path);p.add(hop);if(r.target().equals(targetId))return p;if(visited.add(r.target()))q.add(new PathNode(r.target(),p));}}return List.of();}
    private GroundingInterpretation createInterpretation(SemanticLexicalResolution r){var evidence=AliasWeightEvidenceGate.evaluate(contract,minimumAliasWeight==null?1:minimumAliasWeight,r,false);return new GroundingInterpretation(r.steps(),r.confidence(),r.rootEntity(),signature(r.steps()),AliasInterpretationEvidence.from(evidence));}
    private List<GroundingInterpretation> distinctInterpretations(List<SemanticLexicalResolution> raw){return raw.stream().map(this::createInterpretation).collect(java.util.stream.Collectors.toMap(GroundingInterpretation::signature,x->x,(a,b)->a.interpretationScore()>=b.interpretationScore()?a:b,LinkedHashMap::new)).values().stream().sorted(Comparator.comparingDouble(GroundingInterpretation::interpretationScore).reversed().thenComparing(x->Long.toUnsignedString(x.rootEntity().value()))).toList();}
    private static String signature(List<SemanticLexicalStep> steps){return steps.stream().map(x->meaningKind(x.candidate().kind())+":"+x.candidate().canonicalName()+":"+x.candidate().entityId()+":"+x.candidate().fieldId()+":"+x.candidate().relationshipId()+":"+x.candidate().value()).collect(java.util.stream.Collectors.joining("|"));}
    private String identityKey(SemanticLexicalCandidate c){return meaningKind(c.kind())+":"+c.canonicalName()+":"+c.entityId()+":"+c.fieldId()+":"+c.relationshipId()+":"+c.value();}
    private static SemanticLexicalCandidate better(SemanticLexicalCandidate a,SemanticLexicalCandidate b){if(a.score()!=b.score())return a.score()>b.score()?a:b;return candidateComparator().compare(a,b)<=0?a:b;}
    private static Comparator<SemanticLexicalCandidate> candidateComparator(){return Comparator.comparingDouble(SemanticLexicalCandidate::score).reversed().thenComparingInt(x->kindPreference(x.kind())).thenComparing(SemanticLexicalCandidate::canonicalName,String.CASE_INSENSITIVE_ORDER);}
    private static int kindPreference(SemanticLexicalCandidateKind k){return k==SemanticLexicalCandidateKind.ENTITY?0:k==SemanticLexicalCandidateKind.NODE?1:2;}
    private static SemanticLexicalCandidateKind meaningKind(SemanticLexicalCandidateKind k){return k==SemanticLexicalCandidateKind.NODE?SemanticLexicalCandidateKind.ENTITY:k;}
    private static String[] tokenize(String e){return Arrays.stream(e.split("\\s+")).map(x->x.replaceAll("^[,.;:?!()\\[\\]{}]+|[,.;:?!()\\[\\]{}]+$","" )).filter(x->!x.isEmpty()).toArray(String[]::new);}
    private GroundingDecision budgetDecision(String expression,String reason,GroundingBudgetLimit limit){return new GroundingDecision(expression,GroundingOutcome.BUDGET_EXCEEDED,null,List.of(),reason,List.of(),limit,List.of(),null,List.of());}
    private record Transition(double factor,List<SemanticLexicalCandidate> path){}
    private record PathNode(EntityId entity,List<SemanticLexicalCandidate> path){}
    private record SearchState(EntityId rootEntity,EntityId currentEntity,List<SemanticLexicalStep> steps,double graphFactor){SearchState add(SemanticLexicalStep s,double f){var current=s.candidate().targetEntityId()!=null?s.candidate().targetEntityId():s.candidate().entityId()!=null?s.candidate().entityId():s.candidate().sourceEntityId();var ns=new ArrayList<>(steps);ns.add(s);return new SearchState(rootEntity,current,List.copyOf(ns),graphFactor*f);}}
    private static final class SearchBudget {final int max;final long timeout;final ISemanticLexicalCandidateSource.CancellationToken cancellation;final long start=System.nanoTime();int nodes;boolean exceeded;GroundingBudgetLimit limitHit=GroundingBudgetLimit.NONE;SearchBudget(int m,long t,ISemanticLexicalCandidateSource.CancellationToken c){max=m;timeout=t;cancellation=c;}boolean tick(){if(exceeded)return true;if(cancellation!=null&&cancellation.isCancellationRequested()){exceeded=true;limitHit=GroundingBudgetLimit.CANCELLED;return true;}if(++nodes>max){exceeded=true;limitHit=GroundingBudgetLimit.MAX_PATHS_EXPLORED;return true;}if((System.nanoTime()-start)/1_000_000>timeout){exceeded=true;limitHit=GroundingBudgetLimit.TIMEOUT;return true;}return false;}}
    private static final class Cancelled extends RuntimeException {}
}
