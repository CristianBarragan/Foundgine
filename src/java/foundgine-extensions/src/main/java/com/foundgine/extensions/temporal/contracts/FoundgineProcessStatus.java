package com.foundgine.extensions.temporal.contracts;

import java.util.List;

public record FoundgineProcessStatus(String process, String state, String currentStep, List<String> completedSteps,
        List<String> compensatedSteps, String pendingApprovalStep, String failure) {
    public FoundgineProcessStatus {
        completedSteps = List.copyOf(completedSteps);
        compensatedSteps = List.copyOf(compensatedSteps);
    }
}
