package com.foundgine.runtime.routing;
import java.time.Duration; import java.util.*;
public record TaskContract(String taskId,TaskExecutionMode mode,TaskRuntimeLocation runtime,TaskWorkerAssignment worker,TaskLifecyclePolicy lifecycle,List<String> policyTags,String resumeWorkerId){
 public TaskContract { policyTags=List.copyOf(policyTags==null?List.of():policyTags); }
 public static TaskContract defaults(String taskId){return new TaskContract(taskId,TaskExecutionMode.FOREGROUND,TaskRuntimeLocation.LOCAL,TaskWorkerAssignment.NEW,TaskLifecyclePolicy.DEFAULT,List.of(),null);}
 public enum TaskExecutionMode { FOREGROUND,BACKGROUND }
 public enum TaskRuntimeLocation { LOCAL,REMOTE,ISOLATED }
 public enum TaskWorkerAssignment { NEW,RESUME }
 public record RetryPolicy(int maxAttempts,Duration initialBackoff,double backoffMultiplier){public static final RetryPolicy NONE=new RetryPolicy(1,Duration.ZERO,1.0);}
 public record TaskLifecyclePolicy(boolean cancelable,boolean observable,RetryPolicy retry){public static final TaskLifecyclePolicy DEFAULT=new TaskLifecyclePolicy(true,true,RetryPolicy.NONE);}
}
