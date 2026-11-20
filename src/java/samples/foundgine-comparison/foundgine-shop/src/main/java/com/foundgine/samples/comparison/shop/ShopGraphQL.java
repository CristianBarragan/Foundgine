package com.foundgine.samples.comparison.shop;

import com.foundgine.extensions.graphql.GraphQLAdapterError;
import com.foundgine.extensions.graphql.GraphQLAdapterErrorCode;
import com.foundgine.extensions.graphql.http.GraphQLHttpHandler.Options;
import java.util.Set;

/** Shop's GraphQL rules; the HTTP handler itself lives in foundgine-extensions. */
final class ShopGraphQL {
    private ShopGraphQL() {}

    static void configure(Options o) {
        // Customers hold OrderLine.read so their own orders can include lines, but only Customer and Order have
        // row-level predicates, so a customer must not START a query at the OrderLine root.
        o.queryRootRoles.put("customer", Set.of("manager", "customer"));
        o.queryRootRoles.put("order", Set.of("manager", "customer"));
        o.queryRootRoles.put("product", Set.of("manager", "customer"));
        o.queryRootRoles.put("orderLine", Set.of("manager"));

        // customer_id is a validated token claim, never a GraphQL argument.
        o.authorize = ctx -> ctx.user().isInRole("customer") && ShopPolicy.customerId(ctx.user()) == null
                ? new GraphQLAdapterError(GraphQLAdapterErrorCode.FORBIDDEN,
                        "A customer_id claim is required for customer operations.", null, GraphQLAdapterErrorCode.FORBIDDEN)
                : null;
    }
}
