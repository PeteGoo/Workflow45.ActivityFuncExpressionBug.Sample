#Activity Func Expression Text Box .Net 4.5 Bug Sample

This sample demonstrates that there is a change in behaviour for the workflow designer between .Net 4.0 and .Net 4.5 that can cause a certain pattern in activity designers to break.

##Scenario
We wanted to create a simplified activity experience for our .Net 4.0 clients to get data from our "Query Service" within a workflow. Our Query Service is a WCF Data Service with a .Net proxy class exposing DataServiceQuery<T> queryable properties. In case you need a refresher on WCF Data Services, ordinarily in code a client would call this proxy API using a LINQ expression and WCF Data Services would turn that into OData URI conventions, send to the server, this would be translated into SQL by the ORM on the other end, data comes back and everyone is happy.

To expose this functionality in Workflow we went with the idea of an activity which had a designer exposing a "Where" filter. The user would enter a VB expression into the Expression Text Box on this designer and the activity would take the responsibility of turning this into an Expression against the appropriate queryable property on the proxy, issuing the query asynchronously and returning the results.

Impementing this was difficult, we could not simply use a normal expression as we needed to execute it asynchronously and allow access to the workfow variables and arguments in scope from within the expression. We found that we could use an ActivityFunc for our Filter as the Expression Text Box would allow a DelegateInArgument to be surfaced in intellisense which represented the item (normally a lambda parameter when writing a LINQ query in code). This made it super easy for the user to enter a LINQ query without having to know our API or too much LINQ. They simply provide a predicate. 

The resulting implementation is quite hard as you need to reconstruct the LINQ expression without knowledge of the Activity Context so that it can be exceuted async. In truth we originally did this by compiling VB on the fly, I have done this sample by sneakily extracting the expression tree from the VB expression when I can and using an Expression Tree visitor pattern which is much better.

##The Sample
The code in this repo shows a simplified version of the above code. It uses a Queryable Source argument to represent the API queryable property discussed above. It omits details that our activity has including the Async implementation of a child AsyncCodeActivity to avoid blocking the Scheduler thread. It also omits our retry logic that we implement on every one of our idempotent network bound custom activities.

##The Bug
###Using Visual Studio 2010 on a machine/vm WITHOUT VS 2012 installed:
When you open the Activity1.xaml file you can see that there is a query activity already on there. You should also be able to see that by entering "item." intellisense will allow you to autocomplete the properties on the type of T where T is the item type of the queryable source.

###Using Visual Studio 2010 on a machine/vm WITH VS 2012 installed or from VS 2012:
The above intellisense will no longer work.

##What I would like
An alternative approach or confirmation that I am pushing poo uphill.