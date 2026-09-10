package com.foundgine.runtime.controlplane.risk;
public record RiskScore(double value,String level){public RiskScore{if(Double.isNaN(value)||value<0||value>1)throw new IllegalArgumentException("Risk score must be between 0 and 1.");}}
