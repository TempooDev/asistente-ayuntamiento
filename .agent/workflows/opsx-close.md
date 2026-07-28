---
description: Close a branch by running PR review, sync, archive, and merge
---

Execute the standard workflow to close a branch and finalize an OpenSpec change.

**Input**: Optionally specify a change name. If omitted, check if it can be inferred from conversation context.

**Steps**:

1. **Verify Git Status & Push**
   - Ensure the working tree is clean.
   - If there are uncommitted changes, ask the user if they should be committed.
   - Ensure the branch is pushed to the remote.

2. **Create Pull Request**
   - Run `gh pr status` to check if a PR exists for the current branch.
   - If no PR exists, create one using `gh pr create --fill` or with an appropriate title and body.

3. **Code Review**
   - Use the `invoke_subagent` tool to spawn the `pr-reviewer` agent to review the PR.
   - Wait for the reviewer's response.
   - **If the reviewer requests changes:**
     - Implement the requested fixes, commit, and push.
     - Ask the reviewer to verify again or assume it's ready if the fixes are trivial.

4. **Sync Specs**
   - Invoke the `/openspec-sync-specs` skill or follow its logic for the current change.
   - Ensure the delta specs (`specs/`) are properly merged into the main specs.

5. **Archive Change**
   - Invoke the `/openspec-archive-change` skill or follow its logic.
   - Move the change directory to `openspec/changes/archive/YYYY-MM-DD-<name>`.

6. **Merge to Main**
   - Run `gh pr merge --merge --delete-branch` (or `--squash`) to merge the PR.
   - Run `git checkout main` and `git pull` to update the local repository.

7. **Summary**
   - Display a summary to the user indicating that the change was successfully reviewed, synced, archived, and merged.
