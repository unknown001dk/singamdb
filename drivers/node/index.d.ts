export class Cursor {
  sort(field: string, direction?: number): this;
  project(fields: string[]): this;
  limit(count: number): this;
  skip(count: number): this;
  toArray(): Promise<any[]>;
}

export class Collection {
  constructor(db: Database, name: string);
  insertOne(doc: Record<string, any>): Promise<any>;
  find(filter?: Record<string, any>): Cursor;
  findOne(filter?: Record<string, any>): Promise<any | null>;
  getById(id: string): Promise<any | null>;
  update(filter: Record<string, any>, update: Record<string, any>): Promise<any>;
  delete(filter: Record<string, any>): Promise<any>;
  createIndex(field: string, type?: string): Promise<any>;
  aggregate(pipeline: any[]): Promise<any[]>;
  explain(filter?: Record<string, any>): Promise<any>;
}

export class Database {
  constructor(client: SingamClient, name: string);
  collection(name: string): Collection;
}

export class SingamClient {
  constructor(connectionString?: string);
  connect(): Promise<void>;
  db(name: string): Database;
  close(): void;
}
