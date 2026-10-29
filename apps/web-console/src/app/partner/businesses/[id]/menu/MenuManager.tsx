'use client';

import { useActionState } from 'react';

import { Button } from '@/components/Button';
import { Alert, Badge, Card } from '@/components/Display';
import { SelectField, TextArea, TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { MenuCategory, MenuItem } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { formatMoney } from '@/lib/labels';

import {
  addCategory,
  addDish,
  changeDish,
  deleteCategory,
  renameCategory,
  updateDish,
} from './actions';

const idle: FormState = { status: 'idle' };

function Problem({ state }: { state: FormState }) {
  return state.status === 'error' && state.message ? (
    <Alert kind="error">{state.message}</Alert>
  ) : null;
}

function DishForm({
  businessId,
  categories,
  item,
  categoryId,
}: {
  businessId: string;
  categories: MenuCategory[];
  item?: MenuItem;
  categoryId?: string;
}) {
  const [state, action] = useActionState<FormState, FormData>(item ? updateDish : addDish, idle);
  const errors = state.fieldErrors ?? {};
  const prefix = item ? `dish-${item.id}` : 'new-dish';

  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      <input type="hidden" name="businessId" value={businessId} />
      {item ? <input type="hidden" name="itemId" value={item.id} /> : null}
      {item ? <input type="hidden" name="version" value={item.version} /> : null}
      <Problem state={state} />
      {state.status === 'success' ? <Alert kind="success">{state.message}</Alert> : null}
      <div className="grid gap-3 sm:grid-cols-2">
        <TextField
          id={`${prefix}-name`}
          name="name"
          label="Name"
          maxLength={120}
          defaultValue={item?.name}
          errors={errors.name}
        />
        <SelectField
          id={`${prefix}-category`}
          name="categoryId"
          label="Category"
          defaultValue={item?.categoryId ?? categoryId ?? ''}
          errors={errors.categoryId}
        >
          <option value="" disabled>
            Choose…
          </option>
          {categories.map((category) => (
            <option key={category.id} value={category.id}>
              {category.name}
            </option>
          ))}
        </SelectField>
        <TextField
          id={`${prefix}-price`}
          name="price"
          type="number"
          step="0.01"
          min={0}
          label="Price (₹)"
          defaultValue={item?.price}
          errors={errors.price}
        />
        <TextField
          id={`${prefix}-tax`}
          name="taxRatePercent"
          type="number"
          step="0.01"
          min={0}
          max={28}
          label="GST (%)"
          hint="Leave blank for the standard 5%. Confirm the right rate with your tax adviser."
          defaultValue={item?.taxRatePercent}
          errors={errors.taxRatePercent}
        />
      </div>
      <TextArea
        id={`${prefix}-description`}
        name="description"
        label="Description"
        rows={2}
        maxLength={500}
        defaultValue={item?.description ?? ''}
        errors={errors.description}
      />
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          name="isVeg"
          defaultChecked={item?.isVeg ?? true}
          className="size-4"
        />
        Vegetarian
      </label>
      <div>
        <SubmitButton pendingLabel="Saving…">{item ? 'Save dish' : 'Add dish'}</SubmitButton>
      </div>
    </form>
  );
}

function DishSwitches({ businessId, item }: { businessId: string; item: MenuItem }) {
  const [state, action] = useActionState<FormState, FormData>(changeDish, idle);

  return (
    <div className="flex flex-col gap-1">
      <Problem state={state} />
      <div className="flex flex-wrap gap-2">
        <form action={action}>
          <input type="hidden" name="businessId" value={businessId} />
          <input type="hidden" name="itemId" value={item.id} />
          <input type="hidden" name="version" value={item.version} />
          <input type="hidden" name="step" value={item.isAvailable ? 'sold-out' : 'available'} />
          <SubmitButton variant="secondary" pendingLabel="…">
            {item.isAvailable ? 'Mark sold out' : 'Back on the menu'}
          </SubmitButton>
        </form>
        <form action={action}>
          <input type="hidden" name="businessId" value={businessId} />
          <input type="hidden" name="itemId" value={item.id} />
          <input type="hidden" name="version" value={item.version} />
          <input type="hidden" name="step" value="archive" />
          <SubmitButton variant="danger" pendingLabel="Removing…">
            Remove from menu
          </SubmitButton>
        </form>
      </div>
    </div>
  );
}

function Dish({
  businessId,
  categories,
  item,
}: {
  businessId: string;
  categories: MenuCategory[];
  item: MenuItem;
}) {
  return (
    <li className="flex flex-col gap-3 border-t border-line pt-3 first:border-t-0 first:pt-0">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="font-semibold">
            <span
              aria-label={item.isVeg ? 'Vegetarian' : 'Non-vegetarian'}
              className={item.isVeg ? 'text-ok' : 'text-danger'}
            >
              ●
            </span>{' '}
            {item.name}
          </p>
          <p className="text-sm text-muted">
            {formatMoney(item.price, item.currency)} · GST {item.taxRatePercent}%
          </p>
          {item.description ? <p className="mt-1 text-sm">{item.description}</p> : null}
        </div>
        {item.isAvailable ? (
          <Badge tone="ok">Available</Badge>
        ) : (
          <Badge tone="warn">Sold out</Badge>
        )}
      </div>
      <DishSwitches businessId={businessId} item={item} />
      <details>
        <summary className="cursor-pointer text-sm font-semibold text-brand">Edit</summary>
        <div className="pt-3">
          <DishForm businessId={businessId} categories={categories} item={item} />
        </div>
      </details>
    </li>
  );
}

function CategoryHeader({ businessId, category }: { businessId: string; category: MenuCategory }) {
  const [renameState, rename] = useActionState<FormState, FormData>(renameCategory, idle);
  const [deleteState, remove] = useActionState<FormState, FormData>(deleteCategory, idle);

  return (
    <div className="flex flex-col gap-2">
      <Problem state={renameState} />
      <Problem state={deleteState} />
      <details>
        <summary className="cursor-pointer text-sm font-semibold text-brand">
          Rename or remove this category
        </summary>
        <div className="flex flex-col gap-3 pt-3">
          <form action={rename} className="flex flex-wrap items-end gap-3" noValidate>
            <input type="hidden" name="businessId" value={businessId} />
            <input type="hidden" name="categoryId" value={category.id} />
            <input type="hidden" name="version" value={category.version} />
            <input type="hidden" name="position" value={category.position} />
            <TextField
              id={`category-${category.id}`}
              name="name"
              label="Name"
              maxLength={80}
              defaultValue={category.name}
              errors={renameState.fieldErrors?.name}
            />
            <SubmitButton variant="secondary" pendingLabel="Saving…">
              Rename
            </SubmitButton>
          </form>
          <form action={remove}>
            <input type="hidden" name="businessId" value={businessId} />
            <input type="hidden" name="categoryId" value={category.id} />
            <Button type="submit" variant="danger">
              Remove category
            </Button>
            <span className="ml-3 text-xs text-muted">
              Only a category that never held a dish can be removed.
            </span>
          </form>
        </div>
      </details>
    </div>
  );
}

export function MenuManager({
  businessId,
  categories,
}: {
  businessId: string;
  categories: MenuCategory[];
}) {
  const [categoryState, createCategory] = useActionState<FormState, FormData>(addCategory, idle);

  return (
    <div className="flex flex-col gap-6">
      {categories.length === 0 ? (
        <Card>
          <p className="text-sm text-muted">
            Your menu is empty. Add a category (such as Starters), then your dishes.
          </p>
        </Card>
      ) : (
        categories.map((category) => (
          <Card key={category.id} title={category.name}>
            <div className="flex flex-col gap-4">
              {category.items.length === 0 ? (
                <p className="text-sm text-muted">No dishes in this category yet.</p>
              ) : (
                <ul className="flex flex-col gap-3">
                  {category.items.map((item) => (
                    <Dish
                      key={item.id}
                      businessId={businessId}
                      categories={categories}
                      item={item}
                    />
                  ))}
                </ul>
              )}
              <CategoryHeader businessId={businessId} category={category} />
            </div>
          </Card>
        ))
      )}

      <Card title="Add a category">
        <form action={createCategory} className="flex flex-wrap items-end gap-3" noValidate>
          <input type="hidden" name="businessId" value={businessId} />
          <Problem state={categoryState} />
          <TextField
            id="new-category"
            name="name"
            label="Name"
            maxLength={80}
            errors={categoryState.fieldErrors?.name}
          />
          <SubmitButton pendingLabel="Adding…">Add category</SubmitButton>
        </form>
      </Card>

      {categories.length > 0 ? (
        <Card title="Add a dish">
          <DishForm businessId={businessId} categories={categories} />
        </Card>
      ) : null}
    </div>
  );
}
