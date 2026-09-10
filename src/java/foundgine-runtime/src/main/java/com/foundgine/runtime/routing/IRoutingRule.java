package com.foundgine.runtime.routing;
public interface IRoutingRule { RouteDecision tryRoute(RoutingContext context); record RouteDecision(boolean matched,TaskContract contract){public static RouteDecision abstain(){return new RouteDecision(false,null);} public static RouteDecision routed(TaskContract c){return new RouteDecision(true,c);}} }
