using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Domain;
using StudyHub.Api.Security;

namespace StudyHub.Api.Data;

/// <summary>
/// Starter notes so a new site isn't empty. They are posted by one clearly named
/// account ("StudyHub Team") rather than by made-up students, and they come with no
/// votes or comments, so everything visitors see from other people is real.
/// They are organised by topic and have no course code, because course codes differ
/// from one university to the next. Students add their own course codes to their notes.
/// </summary>
public static class SampleData
{
    public const string TeamEmail = "team@studyhub.example";
    public const string TeamName = "StudyHub Team";

    private sealed record SampleNote(string Title, string[] Tags, int DaysAgo, string Body);

    /// <summary>
    /// Adds the starter notes. They come in batches: a site gets each batch once, so a site set up
    /// earlier still receives notes added later. A batch is skipped if any of its notes is already
    /// there, so a starter note an admin removed doesn't come back. Returns how many notes were added.
    /// </summary>
    public static async Task<int> SeedAsync(AppDbContext db, IPasswordHasher hasher, CancellationToken ct = default)
    {
        var team = await db.Users.FirstOrDefaultAsync(u => u.Email == TeamEmail, ct);
        var existingTitles = new HashSet<string>();

        if (team is not null)
        {
            // An earlier version gave the starter notes course codes. Clear any that are left.
            var teamNotes = await db.Materials.Where(m => m.AuthorId == team.Id).ToListAsync(ct);
            foreach (var material in teamNotes)
            {
                existingTitles.Add(material.Title);
                material.CourseCode = null;
            }
        }
        else
        {
            team = new User
            {
                Email = TeamEmail,
                DisplayName = TeamName,
                // A random password nobody knows, so nobody can log in as this account.
                PasswordHash = hasher.Hash(Guid.NewGuid().ToString("N"))
            };
            db.Users.Add(team);
        }

        // Reuse tags that already exist; tag names must be unique.
        var tags = await db.Tags.ToDictionaryAsync(t => t.Name, ct);
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var batch in Batches)
        {
            if (batch.Any(note => existingTitles.Contains(note.Title)))
            {
                continue; // this site already has this batch
            }

            foreach (var note in batch)
            {
                var createdAt = now.AddDays(-note.DaysAgo);
                var material = new Material
                {
                    Title = note.Title,
                    Body = note.Body.Trim(),
                    CourseCode = null,
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
                added++;
            }
        }

        await db.SaveChangesAsync(ct);
        return added;
    }

    /// <summary>How many starter notes there are in all.</summary>
    public static int TotalNotes => Batches.Sum(batch => batch.Length);

    /// <summary>The titles of each batch, oldest batch first. Used by the tests.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> BatchTitles
        => Batches.Select(batch => (IReadOnlyList<string>)batch.Select(note => note.Title).ToList()).ToList();

    private static SampleNote[][] Batches => [ComputingNotes, MixedSubjectNotes];

    // Batch 1: computing.
    private static readonly SampleNote[] ComputingNotes =
    [
        new("CPU scheduling: FCFS, SJF and Round Robin", ["os", "scheduling"], 2, """
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

        new("Deadlock: the four conditions and how to break them", ["os", "deadlock"], 5, """
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

        new("Paging and virtual memory in plain words", ["os", "memory"], 9, """
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

        new("Semantic HTML: the tags worth knowing", ["web", "html"], 3, """
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

        new("CSS box model and flexbox cheat sheet", ["web", "css"], 7, """
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

        new("JavaScript promises and async/await", ["web", "javascript"], 12, """
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

        new("Git basics: the commands you use every day", ["git", "tools"], 15, """
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

        new("Project scope and handling change requests", ["project-management", "scope"], 4, """
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

        new("The five phases of an IT project", ["project-management"], 18, """
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

        new("Breadth-first vs depth-first search", ["ai", "search"], 6, """
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

        new("A* search and what makes a good heuristic", ["ai", "search", "heuristics"], 11, """
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

        new("Big-O notation with everyday examples", ["algorithms", "complexity"], 8, """
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

        new("SQL joins explained with two small tables", ["sql", "databases"], 10, """
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

        new("Normalisation: 1NF, 2NF and 3NF", ["databases", "design"], 20, """
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

        new("TCP vs UDP, and the HTTP status codes to remember", ["networks", "http"], 14, """
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

    // Batch 2: other subjects, so the site is useful to students outside computing.
    private static readonly SampleNote[] MixedSubjectNotes =
    [
        new("Active recall and spaced repetition: how to revise", ["study-skills", "revision"], 1, """
            Re-reading notes feels productive, but it mostly builds familiarity, not memory. Two habits work much better.

            Active recall
            Close the notes and try to bring the answer back from memory: answer practice questions, explain the topic out loud, or write down everything you remember on a blank page. The effort of remembering is what makes it stick. Then check what you missed.

            Spaced repetition
            Review the same material several times with growing gaps in between, for example after 1 day, 3 days, 1 week and 2 weeks. Each review comes just as you're starting to forget, which strengthens the memory.

            A simple weekly routine
            1. After each lecture, write 5 questions about it.
            2. Next day, answer them without looking.
            3. Mark the ones you got wrong and revisit those first next time.
            4. A week before the exam, do full past questions under timed conditions.

            Tip: a StudyHub practice quiz is active recall. Write one for each topic and share it with your class.
            """),

        new("Cell structure: organelles and what they do", ["biology", "cells"], 2, """
            Every living thing is made of cells. Animal and plant cells share most parts.

            Nucleus: holds the DNA and controls the cell's activities.
            Cell membrane: a thin layer that controls what enters and leaves the cell.
            Cytoplasm: jelly-like fluid where many chemical reactions happen.
            Mitochondria: release energy from glucose through aerobic respiration. Often called the powerhouse of the cell.
            Ribosomes: make proteins.
            Endoplasmic reticulum: rough ER (with ribosomes) helps make and transport proteins; smooth ER makes lipids.
            Golgi apparatus: packages and sends out proteins.

            Only in plant cells
            Cell wall: made of cellulose, gives the cell strength and shape.
            Chloroplasts: carry out photosynthesis using chlorophyll.
            Large permanent vacuole: filled with cell sap, keeps the cell firm.

            Prokaryotes vs eukaryotes
            Bacteria are prokaryotes: no nucleus (their DNA floats in the cytoplasm) and no membrane-bound organelles such as mitochondria. Animal and plant cells are eukaryotes.
            """),

        new("Mitosis vs meiosis", ["biology", "genetics"], 4, """
            Both are ways a cell divides, but they have different jobs.

            Mitosis
            Purpose: growth, repair and asexual reproduction.
            Divisions: one.
            Result: 2 daughter cells, genetically identical to the parent.
            Chromosome number: stays the same (diploid, 46 in humans).
            Stages: prophase, metaphase, anaphase, telophase (PMAT), then cytokinesis.

            Meiosis
            Purpose: making gametes (sperm and egg cells).
            Divisions: two.
            Result: 4 daughter cells, all genetically different.
            Chromosome number: halved (haploid, 23 in humans), so fertilisation restores 46.
            Variation comes from crossing over (chromosome pairs swapping sections) and the random way pairs line up.

            Quick way to remember: mitosis makes "my toes" (body cells); meiosis makes "me" (a new person).
            """),

        new("The human heart and the path of blood", ["biology", "anatomy", "health"], 6, """
            The heart is a double pump: the right side sends blood to the lungs, the left side sends it to the rest of the body.

            The path of blood, step by step
            1. Deoxygenated blood from the body enters the right atrium through the vena cava.
            2. It passes the tricuspid valve into the right ventricle.
            3. The right ventricle pumps it through the pulmonary artery to the lungs, where it picks up oxygen.
            4. Oxygenated blood returns through the pulmonary veins to the left atrium.
            5. It passes the bicuspid (mitral) valve into the left ventricle.
            6. The left ventricle pumps it out through the aorta to the whole body.

            Things examiners like
            The left ventricle wall is the thickest, because it pumps blood all around the body.
            Valves stop blood flowing backwards.
            Arteries carry blood away from the heart; veins carry it back. The pulmonary artery is the one artery carrying deoxygenated blood.
            """),

        new("Balancing chemical equations step by step", ["chemistry", "equations"], 3, """
            A balanced equation has the same number of each type of atom on both sides, because atoms are not created or destroyed in a reaction.

            Method
            1. Write the correct formulas. Never change them to balance; change only the numbers in front (coefficients).
            2. Count the atoms of each element on each side.
            3. Balance elements that appear in only one substance on each side first. Leave hydrogen and oxygen until last.
            4. Recount everything at the end.

            Example: methane burning
            CH4 + O2 -> CO2 + H2O
            Carbon: 1 and 1, fine.
            Hydrogen: 4 on the left, 2 on the right, so put 2 in front of H2O: CH4 + O2 -> CO2 + 2H2O
            Oxygen: now 2 + 2 = 4 on the right, so put 2 in front of O2.
            Balanced: CH4 + 2O2 -> CO2 + 2H2O

            Check: C 1 = 1, H 4 = 4, O 4 = 4.
            """),

        new("Acids, bases and the pH scale", ["chemistry", "acids"], 9, """
            The pH scale runs from 0 to 14.
            Below 7: acidic. 7: neutral (pure water). Above 7: alkaline (basic).
            Each step is ten times stronger: pH 3 is 10 times more acidic than pH 4.

            Acids release hydrogen ions (H+) in water. Examples: hydrochloric acid, citric acid in lemons, ethanoic acid in vinegar.
            Bases neutralise acids. Alkalis are bases that dissolve in water and release hydroxide ions (OH-). Examples: sodium hydroxide, ammonia solution.

            Neutralisation
            acid + base -> salt + water
            HCl + NaOH -> NaCl + H2O

            Other reactions to know
            acid + metal -> salt + hydrogen
            acid + carbonate -> salt + water + carbon dioxide

            Indicators: litmus turns red in acid and blue in alkali. Universal indicator shows a range of colours for the whole pH scale.
            """),

        new("Newton's three laws with everyday examples", ["physics", "mechanics"], 5, """
            First law (inertia)
            An object stays at rest, or keeps moving at a constant speed in a straight line, unless a resultant force acts on it.
            Example: passengers lurch forward when a trotro brakes suddenly, because their bodies keep moving.

            Second law
            Force = mass x acceleration (F = ma). Force in newtons (N), mass in kg, acceleration in m/s².
            Example: pushing an empty cart is easier than a full one; the same force gives a smaller acceleration to a bigger mass.
            Worked: a 1,000 kg car accelerating at 2 m/s² needs a resultant force of 2,000 N.

            Third law
            When one object exerts a force on another, the second exerts an equal and opposite force on the first.
            Example: when you walk, your foot pushes back on the ground and the ground pushes you forward.
            The two forces act on different objects, so they don't cancel out.
            """),

        new("Differentiation rules cheat sheet", ["maths", "calculus"], 7, """
            Differentiation finds the rate of change: the gradient of a curve at any point.

            Basic rules
            Constant: d/dx (c) = 0
            Power rule: d/dx (x^n) = n x^(n-1)
            Constant multiple: d/dx (a f(x)) = a f'(x)
            Sum: differentiate each term separately.

            Example: y = 3x^4 - 5x^2 + 7x - 2
            dy/dx = 12x^3 - 10x + 7

            Other rules
            Product rule: (uv)' = u'v + uv'
            Quotient rule: (u/v)' = (u'v - uv') / v^2
            Chain rule: dy/dx = dy/du x du/dx. Example: y = (2x + 1)^5 gives dy/dx = 5(2x + 1)^4 x 2 = 10(2x + 1)^4

            Common derivatives
            sin x -> cos x
            cos x -> -sin x
            e^x -> e^x
            ln x -> 1/x

            Turning points: set dy/dx = 0 and solve. If the second derivative is positive it's a minimum; if negative, a maximum.
            """),

        new("Mean, median, mode and standard deviation", ["statistics", "maths"], 10, """
            Data: 4, 8, 6, 5, 3, 8, 9

            Mean: add them up and divide by how many. 43 / 7 = 6.14 (to 2 d.p.)
            Median: the middle value once sorted. Sorted: 3, 4, 5, 6, 8, 8, 9, so the median is 6. With an even count, take the average of the two middle values.
            Mode: the most common value: 8.
            Range: largest minus smallest: 9 - 3 = 6.

            When to use which
            The mean uses every value but is pulled by extreme values (outliers). For incomes or house prices, the median is often fairer.

            Standard deviation
            Measures how spread out the values are around the mean. Small: values are bunched together. Large: values are spread out.
            For a population: square each value's distance from the mean, average those squares (that's the variance), then take the square root.
            For a sample, divide by (n - 1) instead of n.

            In a normal distribution, about 68% of values fall within 1 standard deviation of the mean, and about 95% within 2.
            """),

        new("Supply, demand and equilibrium price", ["economics", "markets"], 8, """
            Law of demand: when the price of a good rises, the quantity demanded falls (all else equal). The demand curve slopes downward.
            Law of supply: when the price rises, the quantity supplied rises, because selling becomes more profitable. The supply curve slopes upward.

            Equilibrium
            Where the two curves cross: the price at which the quantity buyers want equals the quantity sellers offer.
            Above it there's a surplus, and prices tend to fall. Below it there's a shortage, and prices tend to rise.

            Movement vs shift
            A change in the good's own price moves you along the curve.
            Anything else shifts the whole curve.

            Things that shift demand: income, tastes, prices of substitutes and complements, population, expectations.
            Things that shift supply: costs of production, technology, weather (for farm produce), taxes and subsidies, number of sellers.

            Example: a poor harvest shifts the supply of maize to the left, so the equilibrium price rises and the quantity sold falls.
            """),

        new("Price elasticity of demand", ["economics", "elasticity"], 12, """
            Price elasticity of demand (PED) measures how much the quantity demanded responds to a change in price.

            PED = % change in quantity demanded / % change in price
            (The answer is usually negative; economists often quote it without the minus sign.)

            Reading the answer
            Greater than 1: elastic. Buyers react strongly. Example: one brand of soft drink, because there are many substitutes.
            Less than 1: inelastic. Buyers barely react. Example: salt, fuel, medicines.
            Exactly 1: unit elastic.

            Worked example
            Price rises from GH₵10 to GH₵12 (+20%). Quantity falls from 100 to 90 (-10%).
            PED = -10 / 20 = -0.5, so demand is inelastic.

            Why firms care
            If demand is inelastic, raising the price increases total revenue. If it's elastic, raising the price reduces revenue.

            What makes demand more elastic: many close substitutes, a large share of income, luxury rather than necessity, more time to adjust.
            """),

        new("The accounting equation and double entry", ["accounting", "bookkeeping"], 11, """
            The accounting equation
            Assets = Liabilities + Capital (owner's equity)
            Everything a business owns was paid for either by borrowing (liabilities) or by the owner (capital), so the two sides always balance.

            Double entry
            Every transaction affects at least two accounts: one is debited, another is credited, for the same amount.

            Debit increases: assets and expenses (and drawings).
            Credit increases: liabilities, capital and income.

            Examples
            1. Owner puts GH₵5,000 cash into the business: debit Cash, credit Capital.
            2. Buys equipment for GH₵2,000 cash: debit Equipment, credit Cash.
            3. Buys stock on credit for GH₵800: debit Purchases, credit Payables (the supplier).
            4. Makes a cash sale of GH₵600: debit Cash, credit Sales.

            Trial balance: list every account's balance. If total debits equal total credits, the arithmetic of the double entry is right (though some errors, like a missed transaction, still won't show).
            """),

        new("SWOT and PESTLE analysis", ["business", "management"], 14, """
            Both are tools for thinking about a business's situation before making a plan.

            SWOT
            Strengths (internal): what the business does well. Example: a loyal customer base.
            Weaknesses (internal): where it falls short. Example: high staff turnover.
            Opportunities (external): changes it could use. Example: more people shopping on mobile.
            Threats (external): changes that could hurt it. Example: a new competitor nearby.
            Tip: strengths and weaknesses are inside the business; opportunities and threats come from outside.

            PESTLE (looks only at the outside world)
            Political: government policy, stability, trade rules.
            Economic: inflation, exchange rates, interest rates, income levels.
            Social: population, culture, lifestyle trends.
            Technological: new technology, internet access, automation.
            Legal: laws on employment, consumer protection, health and safety.
            Environmental: climate, sustainability, pollution rules.

            A good answer doesn't just list points: it says which ones matter most and what the business should do about them.
            """),

        new("Bloom's taxonomy for writing learning objectives", ["education", "teaching"], 13, """
            Bloom's taxonomy (revised in 2001) sorts thinking skills from simplest to most demanding. Teachers use it to write lesson objectives and questions at the right level.

            1. Remember: recall facts. Verbs: list, define, name, state.
            2. Understand: explain ideas. Verbs: describe, explain, summarise, classify.
            3. Apply: use knowledge in a new situation. Verbs: calculate, solve, demonstrate, use.
            4. Analyse: break information into parts and see how they relate. Verbs: compare, contrast, examine, distinguish.
            5. Evaluate: judge and justify a decision. Verbs: assess, critique, justify, recommend.
            6. Create: put ideas together into something new. Verbs: design, construct, plan, compose.

            Writing a good objective
            Use one measurable verb, and say what learners will be able to do by the end of the lesson.
            Weak: "Learners will understand fractions."
            Better: "By the end of the lesson, learners will be able to add two fractions with different denominators."

            Aim for a mix of levels across a lesson, not only remembering.
            """),

        new("Piaget's stages of cognitive development", ["education", "psychology"], 16, """
            Jean Piaget argued that children think in different ways at different ages, moving through four stages in the same order.

            1. Sensorimotor (birth to about 2 years)
            Learning through the senses and movement. Key milestone: object permanence, knowing an object still exists when it's hidden.

            2. Preoperational (about 2 to 7)
            Language and pretend play grow fast. Thinking is egocentric (hard to see another person's point of view), and children fail conservation tasks: they think water poured into a taller glass is "more".

            3. Concrete operational (about 7 to 11)
            Logical thinking about real, concrete things. Children understand conservation and can sort and order objects.

            4. Formal operational (about 11 and up)
            Abstract and hypothetical thinking: "what if" reasoning and systematic problem-solving.

            For teaching
            Match activities to the stage: hands-on materials for younger learners, abstract problems for older ones.
            Key terms: schema (a mental framework), assimilation (fitting new information into a schema), accommodation (changing a schema to fit new information).

            Criticism: the stages may be less fixed than Piaget thought, and children may be capable earlier when tasks are explained clearly.
            """),

        new("Maslow's hierarchy of needs", ["psychology", "motivation"], 18, """
            Abraham Maslow (1943) suggested that people are motivated by needs arranged in levels, usually drawn as a pyramid. Lower needs generally have to be met before higher ones motivate us.

            From the bottom up
            1. Physiological: food, water, sleep, shelter.
            2. Safety: security, health, a stable income, freedom from danger.
            3. Love and belonging: friendship, family, being part of a group.
            4. Esteem: respect from others, self-confidence, achievement.
            5. Self-actualisation: reaching your full potential.

            Where it's used
            In schools: a hungry or anxious learner struggles to concentrate on lessons.
            At work: managers use it to think about what motivates staff beyond pay.

            Criticisms
            People don't always meet needs strictly in order; someone may pursue creative work while poor.
            The levels may differ across cultures, for example where the group matters more than the individual.
            """),

        new("Writing a strong essay: thesis, structure and PEEL paragraphs", ["english", "writing"], 15, """
            Introduction
            Give brief background, then end with a clear thesis: one or two sentences stating your answer or argument. Everything after should support it.

            Body paragraphs: PEEL
            Point: open with a topic sentence making one clear point.
            Evidence: support it with a quotation, statistic, example or reference.
            Explain: show how the evidence proves your point. This is where the marks are.
            Link: connect back to the thesis, or lead into the next paragraph.

            Conclusion
            Sum up your main points and restate the thesis in new words. Don't add new evidence.

            Habits that lift your grade
            Answer the actual question; underline the command word (discuss, compare, evaluate) before you start.
            Plan for five minutes before writing.
            One idea per paragraph.
            Use linking words: however, furthermore, as a result, in contrast.
            Cite sources in the referencing style your department asks for (often APA or Harvard).
            Leave time to read it through for errors.
            """),

        new("Ghana's road to independence: key events", ["history", "ghana"], 17, """
            1947: the United Gold Coast Convention (UGCC) is formed to push for self-government. J. B. Danquah is a leading figure, and Kwame Nkrumah returns from abroad to become its general secretary.

            28 February 1948: ex-servicemen marching to present a petition to the governor are fired on at the Christiansborg crossroads. Sergeant Adjetey is among those killed, and riots follow in Accra and other towns. The leaders of the UGCC, later called the Big Six, are arrested: Danquah, Nkrumah, Edward Akufo-Addo, Ebenezer Ako Adjei, Emmanuel Obetsebi-Lamptey and William Ofori Atta.

            1949: Nkrumah breaks from the UGCC and founds the Convention People's Party (CPP), demanding "self-government now".

            1950: the CPP launches Positive Action, a campaign of strikes and non-violent protest. Nkrumah is jailed.

            1951: the CPP wins the general election. Nkrumah is released from prison and becomes Leader of Government Business, then Prime Minister in 1952.

            6 March 1957: the Gold Coast becomes independent as Ghana, one of the first African colonies to do so, with Nkrumah as Prime Minister.

            1 July 1960: Ghana becomes a republic, with Nkrumah as its first president.
            """),

        new("The greenhouse effect and climate change", ["geography", "environment"], 19, """
            The greenhouse effect is natural, and without it the Earth would be far too cold for life.

            How it works
            1. Sunlight passes through the atmosphere and warms the Earth's surface.
            2. The surface gives off heat as infrared radiation.
            3. Greenhouse gases (carbon dioxide, methane, water vapour, nitrous oxide) absorb some of this heat and send it back towards the surface, keeping the planet warm.

            The enhanced greenhouse effect
            Human activities add extra greenhouse gases, so more heat is trapped and average temperatures rise.
            Main sources: burning fossil fuels (coal, oil, gas) for power and transport, deforestation (fewer trees to absorb CO2), and farming (methane from cattle and rice paddies).

            Effects
            Rising sea levels, threatening coastal areas.
            More extreme weather: droughts, floods, heatwaves.
            Changing rainfall, affecting farming and food supply.

            Responses
            Mitigation (reducing the cause): renewable energy, planting trees, efficient transport.
            Adaptation (living with the effects): sea defences, drought-resistant crops, better water storage.
            """),
    ];
}
