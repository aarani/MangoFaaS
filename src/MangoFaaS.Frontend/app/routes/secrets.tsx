import { useState, useEffect, useCallback } from "react"
import { useAuth } from "~/lib/auth-context"
import {
  secretsApi,
  functionsApi,
  type SecretItem,
  type SecretValue,
  type FunctionItem,
  type FunctionSecretBinding,
} from "~/lib/api"
import { Button } from "~/components/ui/button"
import { Input } from "~/components/ui/input"
import { Label } from "~/components/ui/label"
import { Badge } from "~/components/ui/badge"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "~/components/ui/table"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "~/components/ui/dialog"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "~/components/ui/select"

function SecretForm({
  initial,
  onSubmit,
  submitting,
  error,
}: {
  initial?: Partial<SecretValue>
  onSubmit: (data: { name: string; value: string; description?: string }) => void
  submitting: boolean
  error: string | null
}) {
  const isEdit = !!initial
  const [name, setName] = useState(initial?.name ?? "")
  const [value, setValue] = useState(initial?.value ?? "")
  const [description, setDescription] = useState(initial?.description ?? "")

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    onSubmit({ name, value, description: description || undefined })
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-3">
      {error && <p className="text-sm text-destructive">{error}</p>}
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="s-name">Name</Label>
        <Input
          id="s-name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
          disabled={isEdit}
          placeholder="MY_SECRET_KEY"
        />
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="s-value">Value</Label>
        <Input
          id="s-value"
          type="password"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          required
          placeholder="secret value"
        />
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="s-desc">Description</Label>
        <Input
          id="s-desc"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Optional description"
        />
      </div>
      <DialogFooter>
        <Button type="submit" disabled={submitting}>
          {submitting ? "Saving…" : "Save"}
        </Button>
      </DialogFooter>
    </form>
  )
}

function BindingsDialog({
  secret,
  open,
  onOpenChange,
  token,
  functions,
}: {
  secret: SecretItem
  open: boolean
  onOpenChange: (open: boolean) => void
  token: string
  functions: FunctionItem[]
}) {
  const [bindings, setBindings] = useState<FunctionSecretBinding[]>([])
  const [loading, setLoading] = useState(true)
  const [selectedFn, setSelectedFn] = useState("")
  const [adding, setAdding] = useState(false)

  const loadBindings = useCallback(async () => {
    setLoading(true)
    try {
      const allBindings: FunctionSecretBinding[] = []
      await Promise.all(
        functions.map(async (fn) => {
          const bs = await secretsApi.getFunctionSecrets(token, fn.id)
          allBindings.push(...bs.filter((b) => b.secretId === secret.id))
        }),
      )
      setBindings(allBindings)
    } catch {
      // ignore
    } finally {
      setLoading(false)
    }
  }, [token, functions, secret.id])

  useEffect(() => {
    if (open) loadBindings()
  }, [open, loadBindings])

  async function handleAdd() {
    if (!selectedFn) return
    setAdding(true)
    try {
      await secretsApi.addSecretToFunction(token, selectedFn, secret.id)
      setSelectedFn("")
      await loadBindings()
    } catch {
      // ignore
    } finally {
      setAdding(false)
    }
  }

  async function handleRemove(binding: FunctionSecretBinding) {
    try {
      await secretsApi.removeSecretFromFunction(token, binding.functionId, binding.secretId)
      await loadBindings()
    } catch {
      // ignore
    }
  }

  const boundFunctionIds = new Set(bindings.map((b) => b.functionId))
  const availableFunctions = functions.filter((fn) => !boundFunctionIds.has(fn.id))

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>
            Function bindings for <span className="font-mono">{secret.name}</span>
          </DialogTitle>
        </DialogHeader>

        {loading ? (
          <p className="text-sm text-muted-foreground py-4 text-center">Loading...</p>
        ) : bindings.length === 0 ? (
          <p className="text-sm text-muted-foreground py-4 text-center">
            No functions bound to this secret.
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Function</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {bindings.map((b) => (
                <TableRow key={b.id}>
                  <TableCell className="font-medium">
                    {functions.find((fn) => fn.id === b.functionId)?.name ?? (
                      <span className="font-mono text-xs text-muted-foreground">{b.functionId}</span>
                    )}
                  </TableCell>
                  <TableCell className="text-right">
                    <Button variant="destructive" size="sm" onClick={() => handleRemove(b)}>
                      Unbind
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}

        {availableFunctions.length > 0 && (
          <div className="flex items-end gap-2 pt-2 border-t">
            <div className="flex-1 flex flex-col gap-1.5">
              <Label>Add function</Label>
              <Select value={selectedFn} onValueChange={setSelectedFn}>
                <SelectTrigger className="w-full">
                  <SelectValue placeholder="Select a function" />
                </SelectTrigger>
                <SelectContent>
                  {availableFunctions.map((fn) => (
                    <SelectItem key={fn.id} value={fn.id}>
                      {fn.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <Button onClick={handleAdd} disabled={!selectedFn || adding}>
              {adding ? "Binding…" : "Bind"}
            </Button>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}

export default function Secrets() {
  const { token } = useAuth()

  const [secrets, setSecrets] = useState<SecretItem[]>([])
  const [functions, setFunctions] = useState<FunctionItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Create dialog
  const [createOpen, setCreateOpen] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  // Edit dialog
  const [editSecret, setEditSecret] = useState<SecretValue | null>(null)
  const [editError, setEditError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [editLoading, setEditLoading] = useState(false)

  // Delete dialog
  const [deleteSecret, setDeleteSecret] = useState<SecretItem | null>(null)
  const [deleting, setDeleting] = useState(false)

  // Bindings dialog
  const [bindingsSecret, setBindingsSecret] = useState<SecretItem | null>(null)

  const loadSecrets = useCallback(async () => {
    if (!token) return
    setLoading(true)
    setError(null)
    try {
      const [ss, fns] = await Promise.all([
        secretsApi.getSecrets(token),
        functionsApi.getFunctions(token),
      ])
      setSecrets(ss)
      setFunctions(fns)
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load secrets")
    } finally {
      setLoading(false)
    }
  }, [token])

  useEffect(() => {
    loadSecrets()
  }, [loadSecrets])

  async function handleCreate(data: { name: string; value: string; description?: string }) {
    if (!token) return
    setCreating(true)
    setCreateError(null)
    try {
      await secretsApi.createSecret(token, data)
      setCreateOpen(false)
      await loadSecrets()
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : "Failed to create secret")
    } finally {
      setCreating(false)
    }
  }

  async function handleEditClick(secret: SecretItem) {
    if (!token) return
    setEditLoading(true)
    try {
      const full = await secretsApi.getSecret(token, secret.id)
      setEditSecret(full)
    } catch {
      // ignore
    } finally {
      setEditLoading(false)
    }
  }

  async function handleEdit(data: { name: string; value: string; description?: string }) {
    if (!token || !editSecret) return
    setEditing(true)
    setEditError(null)
    try {
      await secretsApi.updateSecret(token, editSecret.id, {
        value: data.value,
        description: data.description,
      })
      setEditSecret(null)
      await loadSecrets()
    } catch (e) {
      setEditError(e instanceof Error ? e.message : "Failed to update secret")
    } finally {
      setEditing(false)
    }
  }

  async function handleDelete() {
    if (!token || !deleteSecret) return
    setDeleting(true)
    try {
      await secretsApi.deleteSecret(token, deleteSecret.id)
      setDeleteSecret(null)
      await loadSecrets()
    } catch {
      // ignore
    } finally {
      setDeleting(false)
    }
  }

  function formatDate(iso: string) {
    return new Date(iso).toLocaleDateString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    })
  }

  return (
    <div className="p-6 md:p-8 max-w-6xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Secrets</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Manage secrets and bind them to your functions.
          </p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>
          Create secret
        </Button>
      </div>

      {error && <p className="text-sm text-destructive">{error}</p>}

      {loading ? (
        <div className="flex items-center justify-center py-16">
          <p className="text-muted-foreground">Loading secrets...</p>
        </div>
      ) : secrets.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed py-16 text-center">
          <p className="text-muted-foreground">No secrets yet</p>
          <p className="text-sm text-muted-foreground/70 mt-1">
            Create a secret to store sensitive configuration values.
          </p>
        </div>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Description</TableHead>
              <TableHead>Created</TableHead>
              <TableHead>Updated</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {secrets.map((s) => (
              <TableRow key={s.id}>
                <TableCell className="font-mono font-medium">{s.name}</TableCell>
                <TableCell className="text-muted-foreground max-w-60 truncate">
                  {s.description || <span className="italic">—</span>}
                </TableCell>
                <TableCell className="text-sm text-muted-foreground">{formatDate(s.createdAt)}</TableCell>
                <TableCell className="text-sm text-muted-foreground">{formatDate(s.updatedAt)}</TableCell>
                <TableCell className="text-right">
                  <div className="flex items-center justify-end gap-2">
                    <Button variant="outline" size="sm" onClick={() => setBindingsSecret(s)}>
                      Bindings
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleEditClick(s)}
                      disabled={editLoading}
                    >
                      Edit
                    </Button>
                    <Button variant="destructive" size="sm" onClick={() => setDeleteSecret(s)}>
                      Delete
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {/* Create dialog */}
      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Create secret</DialogTitle>
          </DialogHeader>
          <SecretForm onSubmit={handleCreate} submitting={creating} error={createError} />
        </DialogContent>
      </Dialog>

      {/* Edit dialog */}
      <Dialog open={editSecret !== null} onOpenChange={(o) => !o && setEditSecret(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Edit secret</DialogTitle>
          </DialogHeader>
          {editSecret && (
            <SecretForm
              initial={editSecret}
              onSubmit={handleEdit}
              submitting={editing}
              error={editError}
            />
          )}
        </DialogContent>
      </Dialog>

      {/* Delete confirm dialog */}
      <Dialog open={deleteSecret !== null} onOpenChange={(o) => !o && setDeleteSecret(null)}>
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>Delete secret</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            Are you sure you want to delete{" "}
            <span className="font-mono font-medium text-foreground">{deleteSecret?.name}</span>?
            This will also remove all function bindings. This cannot be undone.
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleteSecret(null)}>
              Cancel
            </Button>
            <Button variant="destructive" onClick={handleDelete} disabled={deleting}>
              {deleting ? "Deleting…" : "Delete"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Bindings dialog */}
      {bindingsSecret && (
        <BindingsDialog
          secret={bindingsSecret}
          open={true}
          onOpenChange={(o) => !o && setBindingsSecret(null)}
          token={token!}
          functions={functions}
        />
      )}
    </div>
  )
}
