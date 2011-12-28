HackCraft.LockFree

A library of lock-free thread-safe collections for .NET and Mono.

This library aims to provide a set of general-purpose and specialised generic collections,
all of which are thread-safe for multiple simultaneous readers and writers without locking.

Collections of purely experimental interest are acceptable, but the aim is that the
majority should be of practical use.

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
HackCraft.LockFree is the optimal solution. E.g. a lock-free dictionary would be considered
successful if it was normally slower than System.Collections.Concurrent.ConcurrentDictionary
but faster when a small number of keys becomes “interesting” and the subject of many writes
to the same key. (Acutally, the implementation at the time of writing beats
ConcurrentDictionary for at least some low-contention cases).

History:

Dr. Cliff Click published details of a lock-free hash-table that he has implemented in Java
(see <http://video.google.com/videoplay?docid=2139967204534450862> and 
<http://www.azulsystems.com/events/javaone_2007/2007_LockFreeHash.pdf>). The similarities
between the JVM and CLR are great enough that a .NET version seemed likely to be fruitful,
especially since ConcurrentDictionary wasn’t available at the time. I produced a relatively
straight port, with the main differences being that hash codes were stored adjacent to the
keys and values (something Click described as desirable but “hard to do in Java” — .NET
value types make this easy), and it’s using the IEqualityComparer<T>-based approach to
determining equality that is the norm in .NET and Mono. The class saw practical use in
projects for my employers.

Considering the heavy use of casting (which means boxing when the types involved are value)
undesirable, I decided to approach the problem from scratch for a more purely .NET-oriented
algorithm. Generic type-safe key-value nodes avoids casting at the cost of extra
allocations. Banning hash values of zero (substituting another value when it arises) allows
the memoised hash values to be considered a first-class member of the record, short-cutting
on more key misses and reliably identifying empty slots by hash code alone. This and some
efforts to reduce allocations during copies due to resizing offset the cost of allocating
when putting.

To-Do:

Testing! Lots of Testing! This is library should currently be considered alpha. Both
practical tests on different hardware and theoretcial analysis is eagerly welcomed. The
NUnit tests definitely need to be extensively fleshed out.

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