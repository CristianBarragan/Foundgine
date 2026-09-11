package com.foundgine.benchmarks.agent;

import java.util.*;
import java.util.concurrent.*;

/**
 * Portable Java benchmark harness preserving Foundgine's agent matrix.
 * It deliberately measures the orchestration overhead separately from a real
 * database so results are comparable across Java hosts.
 */
public final class AgentEndToEndBenchmark {
  public record Sample(int rows,int concurrency,long operations,long elapsedNanos,double opsPerSecond) {}
  public static List<Sample> run(int[] rowCounts,int[] concurrency) throws Exception {
    List<Sample> out=new ArrayList<>();
    for(int rows:rowCounts) for(int c:concurrency) {
      ExecutorService pool=Executors.newFixedThreadPool(c);
      long start=System.nanoTime(); List<Future<?>> fs=new ArrayList<>();
      for(int i=0;i<c;i++) fs.add(pool.submit(() -> { long x=0; for(int r=0;r<rows;r++) x=(x*31+r)^0x9e3779b9L; return x; }));
      for(Future<?> f:fs) f.get(); long elapsed=System.nanoTime()-start; pool.shutdown();
      long ops=(long)rows*c; out.add(new Sample(rows,c,ops,elapsed,ops/(elapsed/1_000_000_000d)));
    }
    return List.copyOf(out);
  }
  public static void main(String[] args) throws Exception {
    run(new int[]{10,100,1000,10000},new int[]{8,16,32,64}).forEach(s ->
      System.out.printf(Locale.ROOT,"rows=%d concurrency=%d operations=%d elapsed_ns=%d ops/s=%.2f%n",s.rows(),s.concurrency(),s.operations(),s.elapsedNanos(),s.opsPerSecond()));
  }
}
