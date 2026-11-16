using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Domain;
using StudyHub.Api.Security;

namespace StudyHub.Api.Data;

/// <summary>
/// Starter notes so a new site isn't empty. They are posted by one clearly named
/// account ("StudyHub Team") rather than by made-up students, and they come with no
/// votes or comments, so everything visitors see from other people is real.
/// </summary>
public static class SampleData
{
    public const string TeamEmail = "team@studyhub.example";
    public const string TeamName = "StudyHub Team";

    private sealed record SampleNote(string CourseCode, string Title, string[] Tags, int DaysAgo, string Body);

    /// <summary>Adds the starter notes once. Running it again does nothing. Returns how many notes were added.</summary>
    public static async Task<int> SeedAsync(AppDbContext db, IPasswordHasher hasher, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(u => u.Email == TeamEmail, ct))
        {
            return 0;
        }

        var team = new User
        {
            Email = TeamEmail,
            DisplayName = TeamName,
            // A random password nobody knows, so nobody can log in as this account.
            PasswordHash = hasher.Hash(Guid.NewGuid().ToString("N"))
        };
        db.Users.Add(team);

        // Reuse tags that already exist; tag names must be unique.
        var tags = await db.Tags.ToDictionaryAsync(t => t.Name, ct);
        var now = DateTime.UtcNow;

        foreach (var note in Notes)
        {
            var createdAt = now.AddDays(-note.DaysAgo);
            var material = new Material
            {
                Title = note.Title,
                Body = note.Body.Trim(),
                CourseCode = note.CourseCode,
                AuthorId = team.Id,
                Author = team,
                CreatedAtUtc = createdAt,
                UpdatedAtUtc = createdAt
            };

            foreach (var name in note.Tags)
            {
                if (!tags.TryGetValue(name, out var tag))
                {
                    tag = new Tag { Name = name };
                    tags[name] = tag;
                    db.Tags.Add(tag);
                }

                material.Tags.Add(tag);
            }

            db.Materials.Add(material);
        }

        await db.SaveChangesAsync(ct);
        return Notes.Length;
    }

    private static readonly SampleNote[] Notes =
    [
        new("CSC 301", "CPU scheduling: FCFS, SJF and Round Robin", ["os", "scheduling"], 2, """
            CPU scheduling decides which ready process runs next.

            FCFS (first come, first served)
            Runs processes in arrival order. Simple, but one long job at the front makes everyone behind it wait. This is called the convoy effect.

            SJF (shortest job first)
            Runs the process with the shortest next CPU burst. It gives the lowest average waiting time, but long jobs can starve, and the burst length has to be estimated.

            Round Robin
            Each process gets a fixed time slice (the quantum), then goes to the back of the queue. Fair and responsive. If the quantum is too small, time is wasted on context switches. If it is too large, it behaves like FCFS.

            Worked example: three jobs arrive together with bursts of 6, 3 and 1 ms.
            FCFS waiting times: 0, 6, 9. Average 5 ms.
            SJF runs them as 1, 3, 6. Waiting times: 0, 1, 4. Average 1.67 ms.
            """),

        new("CSC 301", "Deadlock: the four conditions and how to break them", ["os", "deadlock"], 5, """
            A deadlock is when processes wait on each other forever. It can only happen if all four of these hold at the same time:

            1. Mutual exclusion: a resource can be used by one process at a time.
            2. Hold and wait: a process keeps what it has while asking for more.
            3. No preemption: a resource can't be taken away by force.
            4. Circular wait: A waits for B, B waits for C, and C waits for A.

            Ways to deal with it
            Prevention: make one condition impossible. The usual trick is to give every resource a number and always lock in increasing order, which removes circular wait.
            Avoidance: only grant a request if the system stays in a safe state (the Banker's algorithm).
            Detection and recovery: let it happen, look for a cycle in the resource allocation graph, then stop or roll back one process.

            Exam tip: if a question asks whether deadlock is possible, check the four conditions one by one.
            """),

        new("CSC 301", "Paging and virtual memory in plain words", ["os", "memory"], 9, """
            Virtual memory lets a program act as if it has one big block of memory, even when physical RAM is smaller or shared.

            Key terms
            Page: a fixed-size block of a program's virtual memory.
            Frame: a block of physical RAM of the same size.
            Page table: the map from each page to the frame that holds it.
            Page fault: the program touches a page that isn't in RAM, so the OS loads it from disk.
            TLB: a small cache of recent page-to-frame translations, so the page table isn't read every time.

            With 4 KB pages, the lowest 12 bits of an address are the offset inside the page and the rest is the page number.

            When RAM is full, a page has to be replaced:
            FIFO removes the oldest page.
            LRU removes the page that hasn't been used for the longest time.
            Optimal removes the page that won't be needed for the longest time. It can't be built, but it is the benchmark.

            Thrashing is when the system spends more time swapping pages than doing work.
            """),

        new("CSC 305", "Semantic HTML: the tags worth knowing", ["web", "html"], 3, """
            Semantic tags say what a piece of content is, not how it looks. Browsers, search engines and screen readers all use that meaning.

            Page structure
            header: the top of the page or of a section.
            nav: a block of navigation links.
            main: the main content. Use it once per page.
            article: something that stands on its own, like a post or a note.
            section: a themed group of content, usually with a heading.
            aside: side content such as related links.
            footer: the bottom of the page or of a section.

            Habits that earn marks
            Use one h1 per page and don't skip heading levels.
            Use button for actions and a for links to another page.
            Give every input a label.
            Give every meaningful image alt text.

            A page built from div and span alone can look the same, but it tells assistive technology nothing.
            """),

        new("CSC 305", "CSS box model and flexbox cheat sheet", ["web", "css"], 7, """
            Box model
            Every element is a box with four layers, from the inside out: content, padding, border, margin.
            By default, width only covers the content. Set box-sizing: border-box and width includes padding and border too, which is much easier to reason about.

            Flexbox
            Put display: flex on the parent. Its children become flex items laid out in a row.

            flex-direction: row or column. This sets the main axis.
            justify-content: positions items along the main axis (flex-start, center, space-between).
            align-items: positions items on the cross axis, which is the other direction (stretch, center, flex-start).
            gap: space between items.
            flex-wrap: wrap lets items move to the next line.
            flex: 1 on a child makes it grow to fill the spare space.

            Centre anything
            display: flex; justify-content: center; align-items: center;

            Use flexbox for one direction at a time. Use grid when you need rows and columns together.
            """),

        new("CSC 305", "JavaScript promises and async/await", ["web", "javascript"], 12, """
            A promise is a value that will be ready later, for example the reply to a network request. It is pending, then either fulfilled (it worked) or rejected (it failed).

            async and await make promises read like normal code:

            async function loadNotes() {
              const response = await fetch("/api/notes");
              if (!response.ok) throw new Error("Request failed");
              return response.json();
            }

            What to remember
            An async function always returns a promise.
            await pauses that function only. The rest of the page keeps running.
            Wrap await calls in try and catch to handle failures.
            fetch only rejects on network errors. A 404 still counts as a reply, so check response.ok yourself.

            Common mistake: forgetting await. You get a promise back, not the data.

            To run several requests at the same time, use Promise.all with an array of promises.
            """),

        new("CSC 305", "Git basics: the commands you use every day", ["git", "tools"], 15, """
            Git tracks changes in three places: your working folder, the staging area, and the repository history.

            Everyday commands
            git status: what has changed.
            git add . : stage every change.
            git commit -m "message": save a snapshot of what is staged.
            git push: send your commits to GitHub.
            git pull: bring down other people's commits.
            git log --oneline: a short history.

            Branches
            git switch -c feature-name: create a branch and move to it.
            git switch main: go back to main.
            git merge feature-name: bring the branch's work into the branch you are on.

            Good habits
            Commit small and often.
            Write messages that say what changed and why, such as "Fix login error for empty passwords".
            Add a .gitignore so build output and secrets never get committed.
            Pull before you start work, so you don't build on an old copy.
            """),

        new("CSC 317", "Project scope and handling change requests", ["project-management", "scope"], 4, """
            Scope is the agreed list of what a project will deliver, and just as importantly what it will not.

            Useful documents
            Scope statement: deliverables, exclusions, constraints and assumptions.
            Work breakdown structure (WBS): the deliverables split into smaller pieces that can be estimated and assigned.

            Scope creep is when extra work slips in without anyone adjusting time, cost or people. It is one of the most common reasons projects run late.

            Handling a change request
            1. Write the request down.
            2. Work out the impact on schedule, budget and quality.
            3. Take it to whoever has authority (the sponsor or a change control board).
            4. If approved, update the plan and tell the team.
            5. If rejected, record why.

            Remember the triple constraint: scope, time and cost are tied together. A change to one affects at least one of the others.
            """),

        new("CSC 317", "The five phases of an IT project", ["project-management"], 18, """
            Most IT projects move through five phases.

            1. Initiating
            Decide whether the project should happen. Outputs: business case, project charter, key stakeholders.

            2. Planning
            Decide how it will be done. Outputs: scope, schedule, budget, risk register, communication plan.

            3. Executing
            Do the work. Build, test, and keep stakeholders informed.

            4. Monitoring and controlling
            Runs alongside executing. Compare progress with the plan, manage changes, and act when something drifts.

            5. Closing
            Hand over the product, get formal sign-off, release the team, and record lessons learned. A post-implementation review later checks whether the benefits actually arrived.

            A common exam question describes an activity and asks which phase it belongs to. Ask yourself whether it is deciding, planning, doing, checking or finishing.
            """),

        new("CSC 313", "Breadth-first vs depth-first search", ["ai", "search"], 6, """
            Both explore a graph or a state space. They differ in which node they expand next.

            Breadth-first search (BFS)
            Uses a queue. Explores every node one step away, then two steps away, and so on.
            Finds the path with the fewest steps.
            Uses a lot of memory, because it keeps a whole level at once.

            Depth-first search (DFS)
            Uses a stack, or recursion. Follows one path as deep as it goes, then backtracks.
            Uses little memory.
            Doesn't promise the shortest path, and can loop forever unless you record visited nodes.

            Which one?
            Shortest path where every step costs the same: BFS.
            Just need any solution, or memory is tight: DFS.
            Steps have different costs: neither. Use uniform-cost search or A*.

            Tracing tip: write out the queue or stack after every step. Most lost marks come from expanding nodes in the wrong order.
            """),

        new("CSC 313", "A* search and what makes a good heuristic", ["ai", "search", "heuristics"], 11, """
            A* picks the node with the lowest value of f(n) = g(n) + h(n).

            g(n) is the real cost from the start to n.
            h(n) is the heuristic: an estimate of the cost from n to the goal.

            Two properties to know
            Admissible: h never overestimates the true remaining cost. With an admissible heuristic, A* tree search finds the cheapest path.
            Consistent: for every step from n to a neighbour m, h(n) is at most the step cost plus h(m). Every consistent heuristic is also admissible.

            Examples
            Straight-line distance for route finding on a map.
            Manhattan distance on a grid where you can only move up, down, left and right.

            Related searches
            If h is always 0, A* becomes uniform-cost search.
            Greedy best-first search uses h alone. It is often fast, but it can miss the cheapest path.

            A heuristic closer to the true cost, without going over it, means fewer nodes expanded.
            """),

        new("CSC 201", "Big-O notation with everyday examples", ["algorithms", "complexity"], 8, """
            Big-O describes how the work grows as the input size n grows. It ignores constants and focuses on the shape of the growth.

            From fastest to slowest
            O(1): reading an array element by its index.
            O(log n): binary search in a sorted list.
            O(n): checking every item once.
            O(n log n): merge sort.
            O(n^2): two nested loops over the same list, like bubble sort.
            O(2^n): trying every subset of a set.

            Rules
            Drop constants: 3n is O(n).
            Keep the biggest term: n^2 + n is O(n^2).
            Unless told otherwise, give the worst case.

            A feel for the difference: binary search on a million sorted items needs about 20 comparisons. A linear scan might need a million.

            Counting tip: one loop over n is O(n). A loop inside a loop is usually O(n^2). Halving the problem each step gives log n.
            """),

        new("CSC 204", "SQL joins explained with two small tables", ["sql", "databases"], 10, """
            Take two tables.

            students: (1, Ama), (2, Kofi), (3, Esi)
            enrolments: (student 1, Databases), (student 1, Networks), (student 2, Databases)

            INNER JOIN keeps only the rows that match on both sides.
            Result: Ama Databases, Ama Networks, Kofi Databases. Esi is missing because she has no enrolment.

            LEFT JOIN keeps every row from the left table and fills the gaps with NULL.
            Result: the same three rows, plus Esi with NULL for the course.

            RIGHT JOIN is the mirror image. FULL OUTER JOIN keeps unmatched rows from both sides.

            Example
            SELECT s.name, e.course
            FROM students s
            LEFT JOIN enrolments e ON e.student_id = s.id;

            To find students with no course, add: WHERE e.student_id IS NULL

            Tip: decide which table must appear in full, and put that one on the left.
            """),

        new("CSC 204", "Normalisation: 1NF, 2NF and 3NF", ["databases", "design"], 20, """
            Normalisation organises tables so each fact is stored once. That prevents update, insert and delete anomalies.

            First normal form (1NF)
            Every cell holds a single value, and there are no repeating groups. A phones column holding two numbers breaks 1NF.

            Second normal form (2NF)
            In 1NF, and every non-key column depends on the whole primary key. This only matters when the key has more than one column. If the key is (student_id, course_id), then student_name depends on student_id alone, so it belongs in a students table.

            Third normal form (3NF)
            In 2NF, and no non-key column depends on another non-key column. If a table stores department_id and department_name, the name depends on the id, so departments get their own table.

            A way to remember it: every non-key column must depend on the key, the whole key, and nothing but the key.

            Working method: write down the dependencies first, then split the tables.
            """),

        new("CSC 307", "TCP vs UDP, and the HTTP status codes to remember", ["networks", "http"], 14, """
            TCP
            Sets up a connection first with a three-way handshake: SYN, SYN-ACK, ACK.
            Reliable and ordered. Lost data is sent again.
            Has flow control and congestion control.
            Used for web pages, email and file transfer.

            UDP
            No connection and no delivery guarantee.
            Very little overhead, so it is fast.
            Used for video calls, online games and DNS lookups, where a late packet is worse than a lost one.

            HTTP status codes
            200 OK: it worked.
            201 Created: a new thing was made.
            204 No Content: it worked and there is nothing to send back.
            301 Moved Permanently: use the new address from now on.
            400 Bad Request: the request itself is wrong.
            401 Unauthorized: you are not logged in.
            403 Forbidden: you are logged in, but not allowed.
            404 Not Found: there is nothing at that address.
            409 Conflict: it clashes with what is already there.
            500 Internal Server Error: the server broke.

            Quick rule: codes starting with 4 are the client's mistake, and codes starting with 5 are the server's.
            """),
    ];
}
