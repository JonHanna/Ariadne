Ariadne

A library of lock-free thread-safe collections for .NET and Mono.

This library aims to provide a set of general-purpose and specialised generic collections,
all of which are thread-safe for multiple simultaneous readers and writers without locking.

Collections of purely experimental interest are acceptable, but the aim is that the
majority should be of practical use.

Homepage:

Please visit <http://hackcraft.github.com/Ariadne/> for this project’ homepage, including
documentation and binaries.

Aims:

Support for the common .NET/Mono conventions; implementing applicable interfaces, using
IEqualityComparer<T> (with EqualityComparer<T>.Default as a default) whenever an equality
comparison is called for, compatibility with LINQ, etc.

While implemented in C♯ 4.0 for .NET 4.0 and Mono 2.10.6, language features used for the
library should aim to make it relatively easy for someone to adapt the code for use
versions as early as C♯ 2.0 if required (e.g. avoid implicit typing when it provides only
syntactic sugar). Hence providing an alternative to the System.Collections.Concurrent
namespace for those restricted to .NET 2.0 through .NET 3.5.

Performance should be comparable to that offered by System.Collections.Concurrent in most
cases, but not need compete with it in all, with the intention that there be cases where
Ariadne is the optimal solution, even if it isn’t always. E.g. a lock-free dictionary would be considered
successful if it was normally slower than System.Collections.Concurrent.ConcurrentDictionary
but faster when a small number of keys becomes “interesting” and the subject of many writes
to the same key. (Acutally, the implementation at the time of writing beats
ConcurrentDictionary for at least some low-contention cases).

To-Do:

Testing! Lots of Testing! Both practical tests on different hardware and theoretcial analysis is eagerly
welcomed. The NUnit tests definitely need to be extensively fleshed out.

More classes for the library. I’ve a few classes planned that will add functionality it
will be sensible to include in such a library, but there should be plenty of scope for
more.

Heuristic adjustments. In particular the values that set when a hash-table needs to be
resized are determined from a small amount of experimentation, and may not be those with
the most general balance.

Performance improvements that encourage people to mis-quote Knuth about “premature
optimisation”! More seriously, the purpose of this library makes changes purely for minor
performance gains, justifiable. Such improvements should still be balanced with concerns
for readability, in no way compromise reliability, and optimisations that improve one case
and the cost of another require more justification than just the case in which they bring
an improvement. Still, while some projects will wisely reject changes made purely for a
minute performance gain, such changes will not be rejected out of hand here.